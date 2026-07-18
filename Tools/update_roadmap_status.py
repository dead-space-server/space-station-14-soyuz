import re, os, base64

ROADMAP = "Resources/Prototypes/_DeadSpace/_Soyuz/roadmap.yml"

desc_b64 = os.environ.get("DESCRIPTION_B64", "")
desc = base64.b64decode(desc_b64).decode("utf-8", errors="replace")
desc = re.sub(r'<!--.*?-->', '', desc, flags=re.DOTALL)

if ":world_map:" not in desc:
    print("Изменений в дорожной карте нету, выход...")
    exit(0)

match = re.search(r':world_map:\s*\n-\s*Заголовок\s*:\s*(.+?)(?:\n|$)', desc, re.IGNORECASE)
if match:
    task_title = match.group(1).strip()
else:
    print("Символ :world_map: не был найден. Выход..")
    exit(0)

with open(ROADMAP, "r", encoding="utf-8") as f:
    lines = f.readlines()

found = False
for i, line in enumerate(lines):
    if f'title: "{task_title}"' in line:
        found = True

        for a in range(i, len(lines)):
            if lines[a].strip().startswith('category:'):
                if 'Completed' in lines[a]:
                    print(f"Задача `{task_title}` уже помечена как выполненная. Выход..")
                    exit(0)

                else:
                    lines[a] = f'  category: Completed\n'
                    print(f"Задача `{task_title}` помечена как выполненная! Выход..")
                    break
        break

if not found:
    print(f"Задача с заголовком `{task_title}` не была найдена. Выход..")
    exit(1)

with open(ROADMAP, "w", encoding="utf-8") as f:
    f.writelines(lines)

print("Дорожная карта обновлена! Выход..")