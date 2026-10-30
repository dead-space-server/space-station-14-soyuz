ent-SoyuzPharmaceuticalLaboratory = pharmaceutical research laboratory
    .desc = A Soyuz-1 medical complex for analyzing substances and developing new medicines.
ent-SoyuzPharmaceuticalLaboratoryMachineCircuitboard = pharmaceutical research laboratory machine board
    .desc = A pharmaceutical research laboratory machine board.
ent-SoyuzPharmaceuticalBiomodel = sealed pharmacological tissue model
    .desc = A human tissue sample for contained trials.
reagent-name-soyuz-experimental-medicine = experimental medicine
reagent-desc-soyuz-experimental-medicine = A new composition produced in a pharmaceutical laboratory.
reagent-name-soyuz-pharmaceutical-residue = pharmaceutical waste
reagent-desc-soyuz-pharmaceutical-residue = Unusable production residue.

pharma-title = Pharmaceutical Research Laboratory
pharma-subtitle = SOYUZ-1 · MEDICAL RESEARCH COMPLEX
pharma-input = Sample
pharma-output = Finished medicine
pharma-slot-empty = No sample
pharma-slot-volume = { $volume } / { $capacity } u
pharma-eject = Take beaker
pharma-ready = Ready
pharma-offline = No power
pharma-cancel = Cancel
pharma-amount = Input mixture, u
pharma-tab-sample = Sample
pharma-tab-studied = Analyzed substances
pharma-tab-synthesis = Create
pharma-tab-assay = Trials
pharma-tab-registry = Medicine registry
pharma-tab-journal = Journal
pharma-analyze = Analyze
pharma-synthesize = Create medicine
pharma-assay = Test 1.0 u
pharma-purify = Purify medicine
pharma-register = Register medicine
pharma-reproduce = Produce batch
pharma-print = Print passport
pharma-dispose = Remove waste
pharma-composition = New medicine ingredients
pharma-drug-name = Medicine name
pharma-name-placeholder = Name
pharma-search = Search substances
pharma-analyzed = Analyzed
pharma-not-analyzed = Not analyzed
pharma-untested = Not tested
pharma-studies-empty = No analyzed substances
pharma-registry-empty = No registered medicines
pharma-sample-name = Sample { $id }
pharma-sample-row = { $name } — { $amount } u
pharma-product-description = A laboratory medicine with an individual composition.
pharma-quality = Purity: { $quality }%
pharma-unit-report = Effect per 1.0 u:
    { $effects }
    { $overdose }
pharma-effect-heal = { $type }: heals { $amount }
pharma-effect-damage = { $type }: damages { $amount }
pharma-no-direct-effect = No direct healing or damage detected.
pharma-no-metabolic-effect = No metabolic effects detected.
pharma-inherited-effects = Additional effects (calculated):
pharma-per-unit = Per 1.0 u: { $effect }
pharma-blood-effect = Per 1.0 u: changes blood volume by { $amount } u.
pharma-bleed-effect = Per 1.0 u: changes bleeding rate by { $amount }.
pharma-reagent-effect = Per 1.0 u: changes the amount of { $name } by { $amount } u.
pharma-temperature-effect = Per 1.0 u: changes body thermal energy by { $amount } J.
pharma-oxygenate-effect = Per 1.0 u: changes oxygen saturation by { $amount }.
pharma-lung-effect = Changes gas contents in the lungs: { $gases }.
pharma-alert-effect = Changes alerts about the body's condition.
pharma-sensation-effect = Produces subjective sensations.
pharma-antialcohol-effect = Interacts with an anti-alcohol implant.
pharma-contact-effect = { $method ->
    [touch] On contact
    [injection] On injection
   *[other] On ingestion
    }: { $effect }
pharma-movement-effect = When metabolizing { $amount } u: walking speed changes by { $walk }%, sprinting by { $sprint }%; { $mode ->
    [remove] reduces duration by
    [add] extends duration by
    [set] sets duration to
   *[other] maintains duration for
    } { $duration } s.
pharma-indefinite = indefinitely
pharma-cycle-effect = When metabolizing { $amount } u (effect strength ×{ $scale }): { $effect }
pharma-effect-undescribed = An additional effect could not be identified.
pharma-condition-min = { $name }: at least { $amount } u
pharma-condition-range = { $name }: between { $min } and { $max } u
pharma-effect-conditions = Conditions: { $conditions }.
pharma-effect-probability = Probability: { $chance }%.
pharma-effect-minimum = Minimum portion required: { $amount } u.
pharma-overdose-limit = Maximum before overdose: { $amount } u
pharma-overdose-unknown = Overdose threshold not established.
pharma-verdict-passed = Check passed.
pharma-verdict-impure = Insufficient purity.
pharma-verdict-toxic = High toxicity.
pharma-verdict-ineffective = Healing effect not confirmed.
pharma-ingredient = { $name }: { $fraction }%
pharma-recipe-title = For { $amount } u of input mixture:
pharma-recipe-row = { $name }: { $amount } u
pharma-examine-product = { $name } ({ $id }): { $amount } u, purity { $quality }%.

pharma-action-analyze = Analyzing sample
pharma-action-synthesize = Creating medicine
pharma-action-separate = Purifying medicine
pharma-action-assay = Testing medicine
pharma-action-register = Registering medicine
pharma-action-reproduce = Producing batch
pharma-action-dispose = Removing waste
pharma-action-print = Printing passport
pharma-action-ejectinput = Returning sample
pharma-action-ejectoutput = Returning medicine
pharma-action-cancel = Cancelling operation
pharma-operation-complete = Done.
pharma-operation-cancelled = Operation cancelled. Consumed trial samples are not returned.
pharma-synthesis-complete = Medicine ready.
pharma-separation-complete = Medicine purified.
pharma-assay-complete = Trial complete. Report saved.
pharma-assay-ineffective = Trial complete. The medicine did not pass the safety or effectiveness check.
pharma-register-complete = Medicine registered.
pharma-error-number = Batch volume must be between 0.1 and 200.0 u.
pharma-error-input = No input sample.
pharma-error-output = No receiving beaker.
pharma-error-capacity = Not enough receiving capacity or too many different compositions.
pharma-error-composition = At least two analyzed components are required.
pharma-error-restricted = A component is not permitted for processing.
pharma-error-ingredients = Insufficient ingredients for the selected batch.
pharma-error-selection = Sample unavailable.
pharma-error-formula = Medicine not registered.
pharma-error-purify = This sample cannot be purified.
pharma-error-assay = A separate experimental medicine sample of at least 1.0 u is required.
pharma-error-assay-interrupted = Trial interrupted. The consumed sample is not returned.
pharma-error-registration = This batch has not passed its trial or needs purification.
pharma-registration-sample = A separate sample of experimental medicine is required in the input beaker.
pharma-registration-untested = This batch has not been tested. Purification requires another test.
pharma-registration-existing = This formula is already registered.
pharma-registration-impure = Purity is below 70.0%. Purification is required.
pharma-registration-toxic = Too many toxic impurities. Purification and another test are required.
pharma-registration-failed = This batch failed testing. The result is saved in the report.
pharma-error-name = Name is empty, already used, or contains invalid characters.
pharma-error-changed = Sample changed. Operation cancelled.
pharma-error-power = Operation interrupted: no power. The consumed sample is not returned.
pharma-error-limit = Registry full.
pharma-error-operation = Equipment failure. Operation stopped.
pharma-error-notification = Result saved, but the equipment update failed.
pharma-journal-analysis = { $author }: sample analyzed.
pharma-journal-synthesis = { $author }: produced { $formula }, purity { $quality }%.
pharma-journal-operation = { $author }: { $action }.
pharma-journal-assay = { $author }: tested { $formula }.
pharma-journal-register = { $author }: registered "{ $name }", { $formula }.
pharma-passport = SOYUZ-1 — PHARMACEUTICAL REGISTRY
    MEDICINE PASSPORT
    Name: { $name }
    Identifier: { $id }
    Researcher: { $author }
    Ingredients: { $ingredients }

    { $findings }
