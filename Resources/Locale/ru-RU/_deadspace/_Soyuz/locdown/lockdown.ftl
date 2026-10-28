# Названия отделов
lockdown-department-command = Командование
lockdown-department-security = Милиция
lockdown-department-medical = Медицинский отдел
lockdown-department-engineering = Инженерный отдел
lockdown-department-science = Научный отдел
lockdown-department-cargo = Снабжение
lockdown-department-service = Сервисный отдел

# Объявления в рацию
lockdown-announce-department = Внимание! Объявлен локдаун в "{ $department }". Отдел будет заблокирован через { $seconds } { $seconds ->
    [one] секунду
    [few] секунды
   *[other] секунд
}.
lockdown-announce-station = Внимание! Внимание! На станции объявлен локдаун! Всему экипажу требуется следовать указаниям руководства коллектива. Все шлюзы, кроме общих шлюзов в коридорах, будут заблокированы через { $seconds } { $seconds ->
    [one] секунду
    [few] секунды
   *[other] секунд
}.
lockdown-cancel-department = Локдаун в "{ $department }" отменён.
lockdown-cancel-station = Локдаун на станции отменён.
lockdown-lifted-department = Локдаун в "{ $department }" снят.
lockdown-lifted-station = Локдаун на станции снят.

# Отказы
lockdown-deny-no-implant = Требуется имплант специального допуска.
lockdown-deny-no-station = Консоль не привязана к станции.
lockdown-deny-console-locked = Консоль временно заблокирована.
lockdown-deny-station-active = На станции уже объявлен локдаун.
lockdown-deny-already = Локдаун уже объявлен.
lockdown-deny-not-key = Подтвердить локдаун станции могут только капитан и начальник милиции.
lockdown-deny-need-captain = Отменить локдаун станции может только капитан.

# Интерфейс
lockdown-ui-title = Консоль безопасности станции
lockdown-ui-departments = Локдаун в отделе
lockdown-ui-station = Локдаун станции (два ключа)
lockdown-ui-start = Объявить
lockdown-ui-cancel = Отменить
lockdown-ui-confirm-station = Подтвердить ключ
lockdown-ui-no-station = Консоль не привязана к станции
lockdown-ui-console-ready = Консоль готова к работе
lockdown-ui-console-locked = Консоль заблокирована: { $time }
lockdown-ui-phase-none = Не объявлен
lockdown-ui-phase-pending = Блокировка через { $time }
lockdown-ui-phase-active = Активен, осталось { $time }
lockdown-ui-key-waiting = { $job }: ожидание подтверждения
lockdown-ui-key-confirmed = { $job }: подтверждено ({ $time })

# Имплант «свободный допуск»
lockdown-freepass-no-lockdown = Эта дверь не заблокирована локдауном.
lockdown-freepass-too-far = Дверь слишком далеко.
lockdown-freepass-alert-level = При текущем уровне тревоги имплант не работает.
