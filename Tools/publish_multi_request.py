#!/usr/bin/env python3

from __future__ import annotations

import argparse
import concurrent.futures
import logging
import os
import random
import sys
import threading
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence

import requests
from requests import Response
from requests.adapters import HTTPAdapter

RELEASE_DIR = "release"

ROBUST_CDN_URL = "https://cdn.deadspace14.net/"
FORK_ID = "dssouz"

DEFAULT_MAX_WORKERS = 1
DEFAULT_RETRY_COUNT = 6
DEFAULT_RETRY_BACKOFF_SECONDS = 5.0
DEFAULT_MAX_BACKOFF_SECONDS = 60.0
DEFAULT_CONNECT_TIMEOUT_SECONDS = 30.0
DEFAULT_READ_TIMEOUT_SECONDS = 60 * 60.0

RETRYABLE_STATUS_CODES = {408, 429, 500, 502, 503, 504}

LOGGER = logging.getLogger("publish")
THREAD_LOCAL = threading.local()


@dataclass(frozen=True)
class PublishConfig:
    fork_id: str
    release_dir: Path
    base_url: str
    version: str
    engine_version: str
    publish_token: str
    max_workers: int
    retries: int
    retry_backoff_seconds: float
    max_backoff_seconds: float
    connect_timeout_seconds: float
    read_timeout_seconds: float

    @property
    def request_timeout(self) -> tuple[float, float]:
        return (self.connect_timeout_seconds, self.read_timeout_seconds)

    def endpoint(self, suffix: str) -> str:
        return f"{self.base_url.rstrip('/')}/{suffix.lstrip('/')}"


@dataclass(frozen=True)
class UploadResult:
    path: Path
    size_bytes: int
    attempts: int
    duration_seconds: float

    @property
    def throughput_mib_per_second(self) -> float:
        if self.duration_seconds <= 0:
            return 0.0
        return bytes_to_mib(self.size_bytes) / self.duration_seconds


class PublishError(RuntimeError):
    pass


class RetryableHttpError(PublishError):
    def __init__(self, response: Response):
        body_preview = response.text[:200].replace("\n", " ").strip()
        message = (
            f"HTTP {response.status_code} {response.reason}"
            + (f" body={body_preview!r}" if body_preview else "")
        )
        super().__init__(message)
        self.status_code = response.status_code


def main() -> int:
    configure_logging()
    config = parse_args()

    stage_started = time.perf_counter()
    files = discover_files(config.release_dir)
    total_bytes = sum(path.stat().st_size for path in files)

    LOGGER.info(
        "publish_plan fork_id=%s version=%s files=%d total_size_mib=%.2f workers=%d retries=%d",
        config.fork_id,
        config.version,
        len(files),
        bytes_to_mib(total_bytes),
        min(config.max_workers, len(files)),
        config.retries,
    )

    upload_results: list[UploadResult] = []
    finish_called = False
    try:
        start_publish(config)
        upload_results = upload_files(config, files)
        finish_called = True
        finish_publish(config)
    except Exception as exc:
        LOGGER.exception(
            "publish_failed version=%s fork_id=%s finish_called=%s",
            config.version,
            config.fork_id,
            "yes" if finish_called else "no",
        )
        write_step_summary(
            config=config,
            planned_file_count=len(files),
            total_bytes=total_bytes,
            total_duration_seconds=time.perf_counter() - stage_started,
            upload_results=upload_results,
            success=False,
        )
        if isinstance(exc, PublishError):
            raise
        raise PublishError(format_exception(exc)) from exc

    total_duration_seconds = time.perf_counter() - stage_started
    total_uploaded_bytes = sum(result.size_bytes for result in upload_results)

    LOGGER.info(
        "publish_complete version=%s files=%d uploaded_mib=%.2f total_duration_s=%.2f aggregate_mib_s=%.2f",
        config.version,
        len(upload_results),
        bytes_to_mib(total_uploaded_bytes),
        total_duration_seconds,
        safe_divide(bytes_to_mib(total_uploaded_bytes), total_duration_seconds),
    )

    write_step_summary(
        config=config,
        planned_file_count=len(files),
        total_bytes=total_bytes,
        total_duration_seconds=total_duration_seconds,
        upload_results=upload_results,
        success=True,
    )
    LOGGER.info("SUCCESS!")
    return 0


def configure_logging() -> None:
    handler = logging.StreamHandler(sys.stdout)
    formatter = logging.Formatter(
        fmt="%(asctime)sZ %(levelname)s %(message)s",
        datefmt="%Y-%m-%dT%H:%M:%S",
    )
    formatter.converter = time.gmtime
    handler.setFormatter(formatter)

    LOGGER.handlers.clear()
    LOGGER.setLevel(logging.INFO)
    LOGGER.addHandler(handler)
    LOGGER.propagate = False


def parse_args() -> PublishConfig:
    parser = argparse.ArgumentParser(
        description="Publish files from the release directory to Robust.Cdn.",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--fork-id", default=os.getenv("PUBLISH_FORK_ID", FORK_ID))
    parser.add_argument("--release-dir", default=os.getenv("PUBLISH_RELEASE_DIR", RELEASE_DIR))
    parser.add_argument("--base-url", default=os.getenv("ROBUST_CDN_URL", ROBUST_CDN_URL))
    parser.add_argument("--version", default=os.getenv("GITHUB_SHA"))
    parser.add_argument("--publish-token", default=os.getenv("PUBLISH_TOKEN"))
    parser.add_argument(
        "--max-workers",
        type=int,
        default=int(os.getenv("PUBLISH_MAX_WORKERS", DEFAULT_MAX_WORKERS)),
    )
    parser.add_argument(
        "--retries",
        type=int,
        default=int(os.getenv("PUBLISH_RETRIES", DEFAULT_RETRY_COUNT)),
    )
    parser.add_argument(
        "--retry-backoff-seconds",
        type=float,
        default=float(
            os.getenv("PUBLISH_RETRY_BACKOFF_SECONDS", DEFAULT_RETRY_BACKOFF_SECONDS)
        ),
    )
    parser.add_argument(
        "--max-backoff-seconds",
        type=float,
        default=float(
            os.getenv("PUBLISH_MAX_BACKOFF_SECONDS", DEFAULT_MAX_BACKOFF_SECONDS)
        ),
    )
    parser.add_argument(
        "--connect-timeout-seconds",
        type=float,
        default=float(
            os.getenv("PUBLISH_CONNECT_TIMEOUT_SECONDS", DEFAULT_CONNECT_TIMEOUT_SECONDS)
        ),
    )
    parser.add_argument(
        "--read-timeout-seconds",
        type=float,
        default=float(
            os.getenv("PUBLISH_READ_TIMEOUT_SECONDS", DEFAULT_READ_TIMEOUT_SECONDS)
        ),
    )

    args = parser.parse_args()

    if not args.version:
        raise PublishError("Missing publish version. Pass --version or set GITHUB_SHA.")
    if not args.publish_token:
        raise PublishError("Missing publish token. Pass --publish-token or set PUBLISH_TOKEN.")
    if args.max_workers < 1:
        raise PublishError("--max-workers must be at least 1.")
    if args.retries < 1:
        raise PublishError("--retries must be at least 1.")

    engine_version = get_engine_version()
    print(f"Version: {VERSION}")
    print(f"Engine version: {engine_version}")
    print(f"Fork: {fork_id}")
    print(f"CDN: {ROBUST_CDN_URL}")

    data = {
        "version": VERSION,
        "engineVersion": engine_version,
    }
    headers = {
        "Content-Type": "application/json"
    }

    print(f"Starting publish...")
    resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/start", json=data, headers=headers)
    if not resp.ok:
        print(f"Publish start FAILED: {resp.status_code} {resp.reason}")
        print(f"Response: {resp.text}")
        resp.raise_for_status()
    print("Publish started OK, uploading files...")

    files = list(get_files_to_publish())
    print(f"Files to upload: {len(files)}")
    for file in files:
        size_mb = os.path.getsize(file) / (1024 * 1024)
        print(f"  Uploading {os.path.basename(file)} ({size_mb:.1f} MB)")
        with open(file, "rb") as f:
            headers = {
                "Content-Type": "application/octet-stream",
                "Robust-Cdn-Publish-File": os.path.basename(file),
                "Robust-Cdn-Publish-Version": VERSION
            }
            resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/file", data=f, headers=headers)

        if not resp.ok:
            print(f"  Upload FAILED: {resp.status_code} {resp.reason}")
            print(f"  Response: {resp.text}")
            resp.raise_for_status()

    print("All files uploaded, finishing publish...")

    data = {
        "version": VERSION
    }
    headers = {
        "Content-Type": "application/json"
    }
    resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/finish", json=data, headers=headers)
    if not resp.ok:
        print(f"Publish finish FAILED: {resp.status_code} {resp.reason}")
        print(f"Response: {resp.text}")
        resp.raise_for_status()

    print("SUCCESS!")


def discover_files(release_dir: Path) -> list[Path]:
    if not release_dir.exists():
        raise PublishError(f"Release directory does not exist: {release_dir}")
    if not release_dir.is_dir():
        raise PublishError(f"Release path is not a directory: {release_dir}")

    entries = sorted(release_dir.iterdir(), key=lambda path: path.name)
    directories = [entry.name for entry in entries if entry.is_dir()]
    if directories:
        joined = ", ".join(directories)
        raise PublishError(
            "Directories inside the release directory are not supported by publish/file: "
            f"{joined}"
        )

    files = [entry for entry in entries if entry.is_file()]
    if not files:
        raise PublishError(f"No files found to publish in {release_dir}")

    for file_path in files:
        LOGGER.info(
            "publish_file_discovered file=%s size_mib=%.2f",
            file_path.as_posix(),
            bytes_to_mib(file_path.stat().st_size),
        )

    return files


def start_publish(config: PublishConfig) -> None:
    LOGGER.info(
        "publish_start_request version=%s engine_version=%s endpoint=%s",
        config.version,
        config.engine_version,
        config.endpoint(f"fork/{config.fork_id}/publish/start"),
    )

    post_json_with_retry(
        config,
        endpoint_suffix=f"fork/{config.fork_id}/publish/start",
        payload={
            "version": config.version,
            "engineVersion": config.engine_version,
        },
        request_name="publish_start",
    )


def finish_publish(config: PublishConfig) -> None:
    LOGGER.info(
        "publish_finish_request version=%s endpoint=%s",
        config.version,
        config.endpoint(f"fork/{config.fork_id}/publish/finish"),
    )

    post_json_with_retry(
        config,
        endpoint_suffix=f"fork/{config.fork_id}/publish/finish",
        payload={"version": config.version},
        request_name="publish_finish",
    )


def post_json_with_retry(
    config: PublishConfig,
    *,
    endpoint_suffix: str,
    payload: object,
    request_name: str,
) -> None:
    endpoint = config.endpoint(endpoint_suffix)

    for attempt in range(1, config.retries + 1):
        attempt_started = time.perf_counter()
        response: Response | None = None
        try:
            response = get_thread_session(config).post(
                endpoint,
                json=payload,
                headers={"Content-Type": "application/json"},
                timeout=config.request_timeout,
            )

            if response.status_code in RETRYABLE_STATUS_CODES:
                raise RetryableHttpError(response)

            response.raise_for_status()
            LOGGER.info(
                "%s_complete attempt=%d duration_s=%.2f",
                request_name,
                attempt,
                time.perf_counter() - attempt_started,
            )
            return
        except Exception as exc:
            duration_seconds = time.perf_counter() - attempt_started
            retryable = is_retryable_exception(exc)
            LOGGER.warning(
                "%s_failure attempt=%d duration_s=%.2f retryable=%s error=%s",
                request_name,
                attempt,
                duration_seconds,
                retryable,
                format_exception(exc),
            )

            if attempt >= config.retries or not retryable:
                raise

            backoff_seconds = calculate_backoff_seconds(
                attempt=attempt,
                base_seconds=config.retry_backoff_seconds,
                max_seconds=config.max_backoff_seconds,
            )
            LOGGER.info(
                "%s_retry_scheduled next_attempt=%d sleep_s=%.2f",
                request_name,
                attempt + 1,
                backoff_seconds,
            )
            time.sleep(backoff_seconds)
        finally:
            if response is not None:
                response.close()


def upload_files(config: PublishConfig, files: Sequence[Path]) -> list[UploadResult]:
    worker_count = min(config.max_workers, len(files))
    total_bytes = sum(path.stat().st_size for path in files)

    LOGGER.info(
        "upload_batch_start files=%d total_size_mib=%.2f workers=%d",
        len(files),
        bytes_to_mib(total_bytes),
        worker_count,
    )

    stop_event = threading.Event()
    upload_started = time.perf_counter()
    results: list[UploadResult] = []
    failures: list[tuple[Path, Exception]] = []

    with concurrent.futures.ThreadPoolExecutor(
        max_workers=worker_count,
        thread_name_prefix="cdn-upload",
    ) as executor:
        future_to_path = {
            executor.submit(upload_file, config, path, stop_event): path for path in files
        }
        for future in concurrent.futures.as_completed(future_to_path):
            file_path = future_to_path[future]
            try:
                results.append(future.result())
            except Exception as exc:
                stop_event.set()
                failures.append((file_path, exc))

    if failures:
        failure_summaries = []
        for path, exc in failures:
            failure_summaries.append(f"{path.name}: {type(exc).__name__}: {exc}")
        joined = "; ".join(failure_summaries)
        raise PublishError(f"One or more file uploads failed: {joined}")

    total_duration_seconds = time.perf_counter() - upload_started
    total_uploaded_bytes = sum(result.size_bytes for result in results)
    LOGGER.info(
        "upload_batch_complete files=%d uploaded_mib=%.2f duration_s=%.2f aggregate_mib_s=%.2f",
        len(results),
        bytes_to_mib(total_uploaded_bytes),
        total_duration_seconds,
        safe_divide(bytes_to_mib(total_uploaded_bytes), total_duration_seconds),
    )

    return sorted(results, key=lambda result: result.path.name)


def upload_file(
    config: PublishConfig,
    file_path: Path,
    stop_event: threading.Event,
) -> UploadResult:
    size_bytes = file_path.stat().st_size

    for attempt in range(1, config.retries + 1):
        if stop_event.is_set():
            raise PublishError(f"Upload cancelled before starting {file_path.name}")

        attempt_started = time.perf_counter()
        LOGGER.info(
            "upload_attempt_start file=%s attempt=%d size_mib=%.2f",
            file_path.name,
            attempt,
            bytes_to_mib(size_bytes),
        )

        response: Response | None = None
        try:
            with file_path.open("rb") as file_handle:
                response = get_thread_session(config).post(
                    config.endpoint(f"fork/{config.fork_id}/publish/file"),
                    data=file_handle,
                    headers={
                        "Content-Type": "application/octet-stream",
                        "Robust-Cdn-Publish-File": file_path.name,
                        "Robust-Cdn-Publish-Version": config.version,
                    },
                    timeout=config.request_timeout,
                )

            if response.status_code in RETRYABLE_STATUS_CODES:
                raise RetryableHttpError(response)

            response.raise_for_status()

            duration_seconds = time.perf_counter() - attempt_started
            result = UploadResult(
                path=file_path,
                size_bytes=size_bytes,
                attempts=attempt,
                duration_seconds=duration_seconds,
            )

            LOGGER.info(
                "upload_attempt_success file=%s attempt=%d duration_s=%.2f throughput_mib_s=%.2f",
                file_path.name,
                attempt,
                duration_seconds,
                result.throughput_mib_per_second,
            )
            return result
        except Exception as exc:
            duration_seconds = time.perf_counter() - attempt_started
            retryable = is_retryable_exception(exc)
            LOGGER.warning(
                "upload_attempt_failure file=%s attempt=%d duration_s=%.2f retryable=%s error=%s",
                file_path.name,
                attempt,
                duration_seconds,
                retryable,
                format_exception(exc),
            )

            if attempt >= config.retries or not retryable or stop_event.is_set():
                stop_event.set()
                raise

            backoff_seconds = calculate_backoff_seconds(
                attempt=attempt,
                base_seconds=config.retry_backoff_seconds,
                max_seconds=config.max_backoff_seconds,
            )
            LOGGER.info(
                "upload_retry_scheduled file=%s next_attempt=%d sleep_s=%.2f",
                file_path.name,
                attempt + 1,
                backoff_seconds,
            )
            time.sleep(backoff_seconds)
        finally:
            if response is not None:
                response.close()

    raise PublishError(f"Upload retries exhausted for {file_path.name}")


def get_thread_session(config: PublishConfig) -> requests.Session:
    session = getattr(THREAD_LOCAL, "session", None)
    if session is None:
        session = requests.Session()
        adapter = HTTPAdapter(pool_connections=1, pool_maxsize=1, max_retries=0)
        session.mount("http://", adapter)
        session.mount("https://", adapter)
        session.headers.update({"Authorization": f"Bearer {config.publish_token}"})
        THREAD_LOCAL.session = session
    return session


def get_engine_version() -> str:
    import xml.etree.ElementTree as ET
    tree = ET.parse(os.path.join("RobustToolbox", "MSBuild", "Robust.Engine.Version.props"))
    version = tree.getroot().find(".//Version").text.strip()
    return version


def write_step_summary(
    *,
    config: PublishConfig,
    planned_file_count: int,
    total_bytes: int,
    total_duration_seconds: float,
    upload_results: Sequence[UploadResult],
    success: bool,
) -> None:
    summary_path = os.getenv("GITHUB_STEP_SUMMARY")
    if not summary_path:
        return

    lines = [
        "## Publish Summary",
        "",
        f"- Status: {'success' if success else 'failure'}",
        f"- Fork: `{config.fork_id}`",
        f"- Version: `{config.version}`",
        f"- Files planned: `{planned_file_count}`",
        f"- Files uploaded: `{len(upload_results)}`",
        f"- Total size: `{bytes_to_mib(total_bytes):.2f} MiB`",
        f"- Total duration: `{total_duration_seconds:.2f} s`",
        "",
    ]

    if upload_results:
        lines.extend(
            [
                "| File | Size (MiB) | Attempts | Duration (s) | Throughput (MiB/s) |",
                "| --- | ---: | ---: | ---: | ---: |",
            ]
        )
        for result in upload_results:
            lines.append(
                f"| `{result.path.name}` | {bytes_to_mib(result.size_bytes):.2f} | "
                f"{result.attempts} | {result.duration_seconds:.2f} | "
                f"{result.throughput_mib_per_second:.2f} |"
            )
        lines.append("")

    summary_file = Path(summary_path)
    existing_text = ""
    if summary_file.exists():
        existing_text = summary_file.read_text(encoding="utf-8")

    summary_file.write_text(existing_text + "\n".join(lines) + "\n", encoding="utf-8")


def calculate_backoff_seconds(*, attempt: int, base_seconds: float, max_seconds: float) -> float:
    exponential = base_seconds * (2 ** (attempt - 1))
    jittered = exponential + random.uniform(0, base_seconds)
    return min(max_seconds, jittered)


def is_retryable_exception(exc: Exception) -> bool:
    if isinstance(exc, RetryableHttpError):
        return True
    if isinstance(exc, requests.Timeout):
        return True
    if isinstance(exc, requests.ConnectionError):
        return True
    return False


def bytes_to_mib(size_bytes: int) -> float:
    return size_bytes / (1024 * 1024)


def safe_divide(numerator: float, denominator: float) -> float:
    if denominator <= 0:
        return 0.0
    return numerator / denominator


def format_exception(exc: BaseException) -> str:
    return f"{type(exc).__name__}: {exc}"


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except PublishError as exc:
        LOGGER.error("publish_error %s", exc)
        raise SystemExit(1)
