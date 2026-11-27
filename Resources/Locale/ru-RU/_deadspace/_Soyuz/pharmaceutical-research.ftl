ent-SoyuzPharmaceuticalLaboratory = фармацевтическая исследовательская лаборатория
    .desc = Медицинский комплекс Союза-1 для изучения веществ, создания и испытания новых препаратов.
ent-SoyuzPharmaceuticalLaboratoryMachineCircuitboard = фармацевтическая исследовательская лаборатория (машинная плата)
    .desc = Плата фармацевтической исследовательской лаборатории.
ent-SoyuzPharmaceuticalBiomodel = герметичная фармакологическая биомодель
    .desc = Образец человеческой ткани для закрытых испытаний.
reagent-name-soyuz-experimental-medicine = экспериментальный препарат
reagent-desc-soyuz-experimental-medicine = Новый состав, полученный в фармацевтической лаборатории.
reagent-name-soyuz-pharmaceutical-residue = фармацевтические отходы
reagent-desc-soyuz-pharmaceutical-residue = Непригодный для повторной переработки остаток производства.

pharma-title = Фармацевтическая исследовательская лаборатория
pharma-subtitle = СОЮЗ-1 · НАУЧНО-МЕДИЦИНСКИЙ КОМПЛЕКС
pharma-input = Образец
pharma-output = Готовый препарат
pharma-slot-empty = Нет образца
pharma-slot-volume = { $volume } / { $capacity } u
pharma-eject = Забрать мензурку
pharma-ready = Готово к работе
pharma-offline = Нет питания
pharma-cancel = Прервать
pharma-amount = Исходная смесь, u
pharma-tab-sample = Образец
pharma-tab-studied = Изученные вещества
pharma-tab-synthesis = Создание
pharma-tab-assay = Испытания
pharma-tab-registry = Реестр препаратов
pharma-tab-journal = Журнал
pharma-analyze = Анализировать
pharma-synthesize = Создать препарат
pharma-assay = Испытать 1.0 u
pharma-purify = Очистить препарат
pharma-register = Зарегистрировать препарат
pharma-reproduce = Изготовить партию
pharma-print = Распечатать паспорт
pharma-dispose = Удалить отходы
pharma-composition = Состав нового препарата
pharma-drug-name = Название препарата
pharma-name-placeholder = Название
pharma-search = Поиск вещества
pharma-analyzed = Изучено
pharma-not-analyzed = Не изучено
pharma-untested = Испытания не проведены
pharma-studies-empty = Нет изученных веществ
pharma-registry-empty = Нет зарегистрированных препаратов
pharma-sample-name = Образец { $id }
pharma-sample-row = { $name } — { $amount } u
pharma-product-description = Лабораторный препарат с индивидуальным составом.
pharma-quality = Чистота: { $quality }%
pharma-unit-report = Действие за 1.0 u:
    { $effects }
    { $overdose }
pharma-effect-heal = { $type }: лечение { $amount }
pharma-effect-damage = { $type }: урон { $amount }
pharma-no-direct-effect = Прямого лечения или урона не обнаружено.
pharma-no-metabolic-effect = Действий при усвоении не обнаружено.
pharma-inherited-effects = Сопутствующие действия (расчёт):
pharma-per-unit = За 1.0 u: { $effect }
pharma-blood-effect = За 1.0 u: изменение объёма крови на { $amount } u.
pharma-bleed-effect = За 1.0 u: изменение силы кровотечения на { $amount }.
pharma-reagent-effect = За 1.0 u: изменение количества вещества «{ $name }» на { $amount } u.
pharma-temperature-effect = За 1.0 u: изменение тепловой энергии тела на { $amount } Дж.
pharma-oxygenate-effect = За 1.0 u: изменение насыщения кислородом на { $amount }.
pharma-lung-effect = Изменяет содержимое газов в лёгких: { $gases }.
pharma-alert-effect = Изменяет сигналы о состоянии организма.
pharma-sensation-effect = Вызывает субъективные ощущения.
pharma-antialcohol-effect = Взаимодействует с антиалкогольным имплантом.
pharma-contact-effect = { $method ->
    [touch] При контакте
    [injection] При инъекции
   *[other] При приёме внутрь
    }: { $effect }
pharma-movement-effect = При усвоении { $amount } u: изменение скорости ходьбы на { $walk }%, бега на { $sprint }%; { $mode ->
    [remove] сокращение действия на
    [add] продление действия на
    [set] установка длительности
   *[other] поддержание действия на
    } { $duration } с.
pharma-indefinite = неограниченно
pharma-cycle-effect = При усвоении { $amount } u (сила действия ×{ $scale }): { $effect }
pharma-effect-undescribed = Дополнительное действие не расшифровано.
pharma-condition-min = { $name }: не менее { $amount } u
pharma-condition-range = { $name }: от { $min } до { $max } u
pharma-effect-conditions = Условия: { $conditions }.
pharma-effect-probability = Вероятность: { $chance }%.
pharma-effect-minimum = Минимальная порция для срабатывания: { $amount } u.
pharma-overdose-limit = Максимум до передозировки: { $amount } u
pharma-overdose-unknown = Порог передозировки не установлен.
pharma-verdict-passed = Проверка пройдена.
pharma-verdict-impure = Недостаточная чистота.
pharma-verdict-toxic = Высокая токсичность.
pharma-verdict-ineffective = Лечебное действие не подтверждено.
pharma-ingredient = { $name }: { $fraction }%
pharma-recipe-title = На { $amount } u исходной смеси:
pharma-recipe-row = { $name }: { $amount } u
pharma-examine-product = { $name } ({ $id }): { $amount } u, чистота { $quality }%.

pharma-action-analyze = Анализ образца
pharma-action-synthesize = Создание препарата
pharma-action-separate = Очистка препарата
pharma-action-assay = Испытание препарата
pharma-action-register = Регистрация препарата
pharma-action-reproduce = Изготовление партии
pharma-action-dispose = Удаление отходов
pharma-action-print = Печать паспорта
pharma-action-ejectinput = Выдача образца
pharma-action-ejectoutput = Выдача препарата
pharma-action-cancel = Прерывание операции
pharma-operation-complete = Готово.
pharma-operation-cancelled = Операция прервана. Израсходованные пробы не возвращаются.
pharma-synthesis-complete = Препарат готов.
pharma-separation-complete = Препарат очищен.
pharma-assay-complete = Испытание завершено. Отчёт сохранён.
pharma-assay-ineffective = Испытание завершено. Препарат не прошёл проверку безопасности или лечебного действия.
pharma-register-complete = Препарат зарегистрирован.
pharma-error-number = Допустимый объём партии: от 0.1 до 200.0 u.
pharma-error-input = Нет исходного образца.
pharma-error-output = Нет приёмной мензурки.
pharma-error-capacity = В приёмной мензурке недостаточно места либо слишком много разных составов.
pharma-error-composition = Нужны минимум два изученных компонента.
pharma-error-restricted = Один из компонентов не допускается к переработке.
pharma-error-ingredients = Недостаточно исходных компонентов для выбранной партии.
pharma-error-selection = Образец недоступен.
pharma-error-formula = Препарат не зарегистрирован.
pharma-error-purify = Очистка этого образца недоступна.
pharma-error-assay = Нужен отдельный образец экспериментального препарата объёмом от 1.0 u.
pharma-error-assay-interrupted = Испытание прервано. Израсходованная проба не возвращается.
pharma-error-registration = Эта партия не прошла испытание либо требует очистки.
pharma-registration-sample = Во входной мензурке нужен отдельный образец экспериментального препарата.
pharma-registration-untested = Эта партия ещё не испытана. После очистки требуется новое испытание.
pharma-registration-existing = Этот состав уже зарегистрирован.
pharma-registration-impure = Чистота ниже 70.0%. Требуется очистка.
pharma-registration-toxic = Слишком много токсичных примесей. Требуется очистка и новое испытание.
pharma-registration-failed = Эта партия не прошла испытание. Результат сохранён в отчёте.
pharma-error-name = Название пустое, уже занято или содержит недопустимые символы.
pharma-error-changed = Образец изменился. Операция отменена.
pharma-error-power = Операция прервана: нет питания. Израсходованная проба не возвращается.
pharma-error-limit = Реестр заполнен.
pharma-error-operation = Сбой оборудования. Операция остановлена.
pharma-error-notification = Результат сохранён, но обновление оборудования завершилось с ошибкой.
pharma-journal-analysis = { $author }: образец проанализирован.
pharma-journal-synthesis = { $author }: получен { $formula }, чистота { $quality }%.
pharma-journal-operation = { $author }: { $action }.
pharma-journal-assay = { $author }: испытан { $formula }.
pharma-journal-register = { $author }: зарегистрирован «{ $name }», { $formula }.
pharma-passport = СОЮЗ-1 — ФАРМАЦЕВТИЧЕСКИЙ РЕЕСТР
    ПАСПОРТ ПРЕПАРАТА
    Название: { $name }
    Обозначение: { $id }
    Исследователь: { $author }
    Состав: { $ingredients }

    { $findings }
