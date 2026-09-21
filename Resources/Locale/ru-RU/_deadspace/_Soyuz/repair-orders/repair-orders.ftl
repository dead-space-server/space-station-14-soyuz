ent-RepairOrdersConsole = консоль ремонтных заказов
    .desc = Позволяет просматривать и принимать инженерные заказы станции.
ent-RepairOrdersComputerCircuitboard = плата консоли ремонтных заказов
    .desc = Компьютерная плата для консоли ремонтных заказов.
ent-RepairStructuralAnalyzer = структурный анализатор
    .desc = Переносной инженерный прибор, подсвечивающий незавершённые задачи структурного ремонта.
ent-RepairOrderRewardCrate = ящик наград за ремонтный заказ
    .desc = Защищённый инженерный ящик с оплатой за выполненный ремонтный заказ.
ent-RepairOrderThrusterMediumFlatpack = упаковка ракетного двигателя
    .desc = Универсальная упаковка для сборки среднего ракетного двигателя.
ent-RepairOrderApuFlatpack = упаковка ВСУ
    .desc = Универсальная упаковка для сборки настенной вспомогательной силовой установки.
ent-RepairOrderRtgFlatpack = упаковка РИТЭГ
    .desc = Универсальная упаковка для сборки радиоизотопного термоэлектрического генератора.
ent-RepairOrderShuttleBoardBundle = комплект шаттловых плат
    .desc = Набор полезных плат для строительства и расширения шаттла.
ent-ConstructionBag = сумка для материалов
    .desc = Прочная поясная сумка для хранения строительных материалов.
ent-ConstructionBagOfHolding = блюспейс-сумка для материалов
    .desc = Блюспейс-сумка исключительной вместимости для строительных материалов.

repair-structural-analyzer-popup-on = Анализатор включён.
repair-structural-analyzer-popup-off = Анализатор выключен.
repair-structural-analyzer-construction-tray-scanner = Т-лучевой сканер
repair-structural-analyzer-remove-entity = Убрать: { $entity }

repair-order-damaged-engineering-sattelite-name = Восстановление орбитального спутника
repair-order-damaged-engineering-sattelite-description = Получен запрос на восстановление объекта. Точный характер повреждений будет установлен после его развёртывания.
repair-order-damaged-cargo-shuttle-name = Восстановление «Динеро Mk.II»
repair-order-damaged-cargo-shuttle-description = Получен запрос на восстановление объекта. Точный характер повреждений будет установлен после его развёртывания.
repair-order-damaged-briggle-name = Восстановление «Бриггл»
repair-order-damaged-briggle-description = Получен запрос на восстановление объекта. Точный характер повреждений будет установлен после его развёртывания.
repair-order-damaged-amber-name = Восстановление «Янтаря»
repair-order-damaged-amber-description = Получен запрос на восстановление объекта. Точный характер повреждений будет установлен после его развёртывания.

repair-orders-window-title = Инженерные ремонтные заказы
repair-orders-next-offer = Следующие предложения:
repair-orders-activation-in-progress = Идёт поиск безопасного места и перенос повреждённого шаттла...
repair-orders-completion-in-progress = Идёт завершение ремонтного заказа...
repair-orders-available-heading = Доступные заказы
repair-orders-active-heading = Активный заказ
repair-orders-completed-heading = Последний завершённый заказ
repair-orders-none-available = Сейчас нет доступных ремонтных заказов.
repair-orders-none-active = Активного ремонтного заказа нет.
repair-orders-none-completed = Завершённых ремонтных заказов пока нет.
repair-orders-missing-prototype = Неизвестный заказ ({ $prototype })
repair-orders-difficulty = Класс работ: { $class } ({ $difficulty }/{ $max })
repair-orders-status-active = Статус: активен
repair-orders-time-remaining = Осталось: { $time }
repair-orders-progress-tasks = Восстановлено: { $completed } / { $total }
repair-orders-progress-percent = { $percent }%
repair-orders-blueprint-unavailable = Эталонный чертёж недоступен.
repair-orders-points = Баллы: { $current } / { $max }
repair-orders-accept = Принять
repair-orders-submit = Сдать заказ
repair-orders-expires-in = Истекает через: { $time }
repair-orders-status-completed = Статус: завершён
repair-orders-status-expired = Статус: срок истёк
repair-orders-rewards-delivered = Ящик с наградой доставлен рядом с консолью сдачи.
repair-orders-rewards-not-delivered = Доставка награды ожидается.
repair-orders-final-tasks = Итог ремонта: { $completed } / { $total }
repair-orders-final-points = Итоговые баллы: { $current } / { $max }
repair-orders-rewards-heading = Рассчитанные награды:
repair-orders-no-rewards = Награды не рассчитаны.
repair-orders-reward-line = { $reward } ×{ $count }

repair-orders-error-access = Требуется инженерный доступ.
repair-orders-error-no-station = Эта консоль не подключена к станции.
repair-orders-error-no-grid = Консоль не установлена на Grid станции.
repair-orders-error-unavailable = Этот ремонтный заказ больше недоступен.
repair-orders-error-active = У станции уже есть активный ремонтный заказ.
repair-orders-error-busy = Сейчас уже выполняется принятие другого ремонтного заказа.
repair-orders-error-load = Не удалось загрузить файл повреждённого шаттла.
repair-orders-error-invalid-grid = Файл повреждённого шаттла не содержит одного пригодного Grid.
repair-orders-error-no-space = Не удалось найти безопасное место для повреждённого шаттла.
repair-orders-error-transfer = Не удалось перенести повреждённый шаттл в игровой мир.
repair-orders-error-prepare = Не удалось подготовить эталонный чертёж. Заказ остался доступен.
repair-orders-error-complete-unavailable = Этот активный ремонтный заказ больше нельзя сдать.
repair-orders-error-complete-busy = Этот ремонтный заказ уже завершается.
repair-orders-error-blueprint = Эталонный чертёж недоступен; заказ остаётся активным.
repair-orders-error-incomplete = Ремонт ещё не завершён. Конструкция не соответствует целевой схеме.
repair-orders-error-occupied = Нельзя сдать ремонтный шаттл, пока на его борту находятся люди.
repair-orders-shuttle-controls-locked = Системы управления заблокированы до завершения ремонтных работ.
repair-orders-error-delivery = Не удалось доставить ящик с наградой; заказ остаётся активным.
repair-orders-success = Ремонтный заказ принят. Повреждённый шаттл прибыл.
repair-orders-complete-success = Ремонтный заказ сдан. Ящик с наградой доставлен рядом с этой консолью.
repair-orders-expired-partial-reward = Срок ремонтных работ истёк. Выдана частичная награда.
repair-orders-expired-no-reward = Срок ремонтных работ истёк. Награда не начислена.

repair-order-object-type-satellite = Спутник
repair-order-object-type-small-shuttle = Малый шаттл
repair-order-object-type-medium-shuttle = Средний шаттл
repair-order-object-name-orbital-communications-satellite = Орбитальный спутник связи
repair-order-object-name-amber = Шаттл быстрого реагирования «Янтарь»
repair-order-object-name-briggle = Патрульный шаттл «Бриггл»
repair-order-object-name-dinero-mk2 = Грузовой шаттл «Динеро MK2»

repair-orders-print-report = Печать отчёта
repair-orders-report-paper-name = отчёт о ремонтных работах
repair-orders-report-title = [head=2]ОТЧЁТ О РЕМОНТНЫХ РАБОТАХ[/head]
repair-orders-report-object-name = [bold]Наименование объекта:[/bold] { $value }
repair-orders-report-object-type = [bold]Тип объекта:[/bold] { $value }
repair-orders-report-order-name = [bold]Заказ:[/bold] { $value }
repair-orders-report-difficulty = [bold]Класс работ:[/bold] { $class } ({ $difficulty }/{ $max })
repair-orders-report-progress = [bold]Состояние ремонта:[/bold] { $percent }%
repair-orders-report-final-progress = [bold]Итоговый прогресс:[/bold] { $percent }%
repair-orders-report-tasks = [bold]Выполнено задач:[/bold] { $completed } / { $total }
repair-orders-report-points = [bold]Набрано баллов:[/bold] { $current } / { $max }
repair-orders-report-final-points = [bold]Итоговые баллы:[/bold] { $current } / { $max }
repair-orders-report-remaining = [bold]Оставшееся время:[/bold] { $time }
repair-orders-report-reward-budget = [bold]Наградной бюджет:[/bold] { $budget }
repair-orders-report-status = [bold]Статус:[/bold] { $status }
repair-orders-report-status-active = В процессе
repair-orders-report-status-completed = Ремонт завершён
repair-orders-report-status-expired = Срок ремонта истёк
repair-orders-report-status-expired-pending = Срок ремонта истёк; ожидается завершение обработки
repair-orders-report-timeout-reason = [bold]Причина завершения:[/bold] Истекло отведённое время на ремонтные работы.
repair-orders-report-partial-reward-note = [bold]Примечание:[/bold] Назначена частичная награда в размере 50% от заработанных баллов.
repair-orders-report-partial-reward-pending-note = [bold]Примечание:[/bold] Рассчитан частичный наградной бюджет в размере 50% от заработанных баллов; завершение обработки ожидается.
repair-orders-error-printer-cooldown = Принтер отчётов ещё не готов.
repair-orders-error-report-unavailable = Отчёт для этого ремонтного заказа недоступен.
repair-orders-error-report-terminal-pending = Результат по истечению срока фиксируется. Попробуйте напечатать отчёт ещё раз через несколько секунд.

repair-orders-object = { $type }: { $name }
repair-orders-damage-heading = Характер повреждений:
repair-orders-damage-unknown = Неуточнённые структурные повреждения
repair-orders-damage-event = • { $event }
repair-orders-damage-event-count = • { $event } ×{ $count }
repair-orders-error-damage = Не удалось подготовить повреждения объекта. Предложение сохранено.

repair-orders-waiver-heading = Технические исключения
repair-orders-waiver-action = Техническое исключение: { $name }
repair-orders-waiver-cancel-action = Отменить исключение: { $name }
repair-orders-waiver-confirm = Исключить «{ $name }» ({ $points } очков) из обязательного ремонта?
    Элемент не принесёт очков. Каждое активное исключение дополнительно удерживает 1% заработанных баллов. Итог не может быть ниже нуля.
    Использовано: { $used } / { $max } очков (лимит — 50% исходной стоимости заказа).
    Штраф после подтверждения: { $percent }%.
repair-orders-waiver-cancel-confirm = Отменить исключение для «{ $name }»? Требование снова станет обязательным.
    Штраф после отмены: { $percent }%.
repair-orders-waiver-confirm-button = Подтвердить
repair-orders-waiver-back = Назад
repair-orders-waiver-unavailable = Требование недоступно, уже выполнено или заказ закрывается.
repair-orders-waiver-limit = Превышен лимит исключений: максимум 50% исходной стоимости заказа.
repair-orders-waiver-unknown = Неуточнённое требование
repair-orders-waiver-summary = Технические исключения: { $count }
    Исключено: { $used } / { $max } очков
    Штраф при сдаче: { $percent }%
    Фактически заработано: { $raw }. После штрафа: { $final }.
repair-orders-waiver-report = Технических исключений: { $count }
    Исключённая стоимость: { $used } / { $max }
    Фактически заработано: { $raw }
    Штраф за технические исключения: { $percent }% ({ $penalty } очков)
    Итоговые баллы: { $final }
repair-orders-waiver-report-entry = • { $name }, клетка ({ $x }, { $y }) — { $points } очков

repair-orders-difficulty-class-1 = Текущий ремонт
repair-orders-difficulty-class-2 = Плановый ремонт
repair-orders-difficulty-class-3 = Расширенный ремонт
repair-orders-difficulty-class-4 = Комплексный ремонт
repair-orders-difficulty-class-5 = Восстановительный ремонт
repair-orders-difficulty-class-6 = Капитальный ремонт
repair-orders-difficulty-class-7 = Специальное восстановление
repair-orders-difficulty-class-8 = Аварийное восстановление
repair-orders-difficulty-class-9 = Критическое восстановление
repair-orders-difficulty-class-10 = Полное восстановление

repair-order-imported-description = Восстановите конструкцию и оборудование объекта по эталонному проекту. Объём работ указан в классе сложности заказа.
repair-order-object-type-terminal = Орбитальный терминал
repair-order-object-type-evacuation-shuttle = Эвакуационный шаттл
repair-order-object-type-command-shuttle = Штабной шаттл
repair-order-object-type-prison-complex = Тюремный комплекс
repair-order-object-type-engineering-shuttle = Инженерный корабль
repair-order-object-type-expedition-shuttle = Экспедиционный корабль
repair-order-caravan-name = Восстановление объекта «Караван»
repair-order-object-name-caravan = Караван
repair-order-launch-name = Восстановление объекта «Баркас»
repair-order-object-name-launch = Баркас
repair-order-blizzard-name = Восстановление объекта «Вьюга»
repair-order-object-name-blizzard = Вьюга
repair-order-courier-name = Восстановление объекта «Курьер»
repair-order-object-name-courier = Курьер
repair-order-north-name = Восстановление объекта «Север»
repair-order-object-name-north = Север
repair-order-frontier-name = Восстановление объекта «Рубеж»
repair-order-object-name-frontier = Рубеж
repair-order-hope-name = Восстановление объекта «Надежда»
repair-order-object-name-hope = Надежда
repair-order-dawn-name = Восстановление объекта «Рассвет»
repair-order-object-name-dawn = Рассвет
repair-order-ark-name = Восстановление объекта «Ковчег»
repair-order-object-name-ark = Ковчег
repair-order-pennant-name = Восстановление объекта «Вымпел»
repair-order-object-name-pennant = Вымпел
repair-order-bastion-name = Восстановление объекта «Бастион»
repair-order-object-name-bastion = Бастион
repair-order-eclipse-name = Восстановление объекта «Затмение»
repair-order-object-name-eclipse = Затмение
repair-order-corsair-name = Восстановление объекта «Корсар»
repair-order-object-name-corsair = Корсар
repair-order-granite-name = Восстановление объекта «Гранит»
repair-order-object-name-granite = Гранит
repair-order-hephaestus-name = Восстановление объекта «Гефест»
repair-order-object-name-hephaestus = Гефест
repair-order-apogee-name = Восстановление объекта «Апогей»
repair-order-object-name-apogee = Апогей
