ent-RepairOrdersConsole = repair orders console
    .desc = Used to review and accept station engineering repair orders.
ent-RepairOrdersComputerCircuitboard = repair orders console computer board
    .desc = A computer printed circuit board for a repair orders console.
ent-RepairStructuralAnalyzer = repair structural analyzer
    .desc = A portable engineering device that highlights unfinished structural repair tasks.
ent-RepairOrderRewardCrate = repair order reward crate
    .desc = A secure engineering crate containing payment for a completed repair order.
ent-RepairOrderThrusterMediumFlatpack = rocket thruster flatpack
    .desc = A flatpack used for constructing a medium rocket thruster.
ent-RepairOrderApuFlatpack = APU flatpack
    .desc = A flatpack used for constructing a wall-mounted auxiliary power unit.
ent-RepairOrderRtgFlatpack = RTG flatpack
    .desc = A flatpack used for constructing a radioisotope thermoelectric generator.
ent-RepairOrderShuttleBoardBundle = shuttle circuit board kit
    .desc = A compact set of circuit boards for constructing and extending a shuttle.
ent-ConstructionBag = construction bag
    .desc = A robust belt bag for carrying construction materials.
ent-ConstructionBagOfHolding = bluespace construction bag
    .desc = A bluespace belt bag with exceptional capacity for construction materials.

repair-structural-analyzer-popup-on = Analyzer enabled.
repair-structural-analyzer-popup-off = Analyzer disabled.
repair-structural-analyzer-construction-tray-scanner = a T-ray scanner
repair-structural-analyzer-remove-entity = Remove: { $entity }

repair-order-damaged-engineering-module-name = Damaged engineering module
repair-order-damaged-engineering-module-description = Restore the compact engineering wreck to its reference structure and return every required system to its proper place.
repair-order-stripped-floor-platform-name = Stripped construction platform
repair-order-stripped-floor-platform-description = Restore steel flooring across the entire stripped five-by-five platform.

repair-order-damaged-engineering-sattelite-name = Orbital satellite restoration
repair-order-damaged-engineering-sattelite-description = A request to restore this object has been received. The damage will be diagnosed after deployment.
repair-order-damaged-cargo-shuttle-name = Restoration of Dinero Mk.II
repair-order-damaged-cargo-shuttle-description = A request to restore this object has been received. The damage will be diagnosed after deployment.
repair-order-damaged-briggle-name = Restoration of Briggle
repair-order-damaged-briggle-description = A request to restore this object has been received. The damage will be diagnosed after deployment.
repair-order-damaged-amber-name = Restoration of Amber
repair-order-damaged-amber-description = A request to restore this object has been received. The damage will be diagnosed after deployment.

repair-orders-window-title = Engineering repair orders
repair-orders-next-offer = Next offers:
repair-orders-activation-in-progress = Locating safe space and transferring the damaged shuttle...
repair-orders-completion-in-progress = Finalizing the repair order...
repair-orders-available-heading = Available orders
repair-orders-active-heading = Active order
repair-orders-completed-heading = Last completed order
repair-orders-none-available = No repair orders are currently available.
repair-orders-none-active = No repair order is active.
repair-orders-none-completed = No repair order has been completed yet.
repair-orders-missing-prototype = Unknown order ({ $prototype })
repair-orders-difficulty = Work class: { $class } ({ $difficulty }/{ $max })
repair-orders-status-active = Status: active
repair-orders-time-remaining = Time remaining: { $time }
repair-orders-progress-tasks = Restored: { $completed } / { $total }
repair-orders-progress-percent = { $percent }%
repair-orders-blueprint-unavailable = Reference blueprint is unavailable.
repair-orders-points = Points: { $current } / { $max }
repair-orders-accept = Accept
repair-orders-submit = Submit order
repair-orders-expires-in = Expires in: { $time }
repair-orders-status-completed = Status: completed
repair-orders-status-expired = Status: expired
repair-orders-rewards-delivered = Reward crate delivered beside the submitting console.
repair-orders-rewards-not-delivered = Reward delivery is pending.
repair-orders-final-tasks = Final repair result: { $completed } / { $total }
repair-orders-final-points = Final points: { $current } / { $max }
repair-orders-rewards-heading = Calculated rewards:
repair-orders-no-rewards = No rewards were calculated.
repair-orders-reward-line = { $reward } ×{ $count }

repair-orders-error-access = Engineering access is required.
repair-orders-error-no-station = This console is not connected to a station.
repair-orders-error-no-grid = The console is not installed on a station grid.
repair-orders-error-unavailable = This repair order is no longer available.
repair-orders-error-active = The station already has an active repair order.
repair-orders-error-busy = Another repair order is currently being activated.
repair-orders-error-load = The damaged shuttle file could not be loaded.
repair-orders-error-invalid-grid = The damaged shuttle file does not contain one usable grid.
repair-orders-error-no-space = No safe location for the damaged shuttle was found.
repair-orders-error-transfer = The damaged shuttle could not be transferred into the game world.
repair-orders-error-prepare = The reference blueprint could not be prepared. The order remains available.
repair-orders-error-complete-unavailable = This active repair order can no longer be submitted.
repair-orders-error-complete-busy = This repair order is already being finalized.
repair-orders-error-blueprint = The repair blueprint is unavailable; the order remains active.
repair-orders-error-incomplete = Repairs are not complete. The structure does not match the target blueprint.
repair-orders-error-occupied = The repair shuttle cannot be dispatched while there are people on board.
repair-orders-shuttle-controls-locked = Shuttle controls are locked until the repair work is completed.
repair-orders-error-delivery = The reward crate could not be delivered; the order remains active.
repair-orders-success = Repair order accepted. The damaged shuttle has arrived.
repair-orders-complete-success = Repair order submitted. The reward crate was delivered beside this console.
repair-orders-expired-partial-reward = Repair time has expired. A partial reward has been issued.
repair-orders-expired-no-reward = Repair time has expired. No reward was issued.

repair-order-object-type-satellite = Satellite
repair-order-object-type-small-shuttle = Small shuttle
repair-order-object-type-medium-shuttle = Medium shuttle
repair-order-object-name-orbital-communications-satellite = Orbital communications satellite
repair-order-object-name-amber = Amber rapid-response shuttle
repair-order-object-name-briggle = Briggle patrol shuttle
repair-order-object-name-dinero-mk2 = Dinero MK2 cargo shuttle

repair-orders-print-report = Print report
repair-orders-report-paper-name = repair work report
repair-orders-report-title = [head=2]REPAIR WORK REPORT[/head]
repair-orders-report-object-name = [bold]Object name:[/bold] { $value }
repair-orders-report-object-type = [bold]Object type:[/bold] { $value }
repair-orders-report-order-name = [bold]Order:[/bold] { $value }
repair-orders-report-difficulty = [bold]Work class:[/bold] { $class } ({ $difficulty }/{ $max })
repair-orders-report-progress = [bold]Repair progress:[/bold] { $percent }%
repair-orders-report-final-progress = [bold]Final progress:[/bold] { $percent }%
repair-orders-report-tasks = [bold]Tasks completed:[/bold] { $completed } / { $total }
repair-orders-report-points = [bold]Points earned:[/bold] { $current } / { $max }
repair-orders-report-final-points = [bold]Final points:[/bold] { $current } / { $max }
repair-orders-report-remaining = [bold]Time remaining:[/bold] { $time }
repair-orders-report-reward-budget = [bold]Reward budget:[/bold] { $budget }
repair-orders-report-status = [bold]Status:[/bold] { $status }
repair-orders-report-status-active = In progress
repair-orders-report-status-completed = Repair completed
repair-orders-report-status-expired = Repair deadline expired
repair-orders-report-status-expired-pending = Repair deadline expired; terminal processing pending
repair-orders-report-timeout-reason = [bold]Termination reason:[/bold] The allotted repair time expired.
repair-orders-report-partial-reward-note = [bold]Note:[/bold] A partial reward equal to 50% of earned points was assigned.
repair-orders-report-partial-reward-pending-note = [bold]Note:[/bold] A partial reward budget equal to 50% of earned points was calculated; terminal processing is pending.
repair-orders-error-printer-cooldown = The report printer is not ready yet.
repair-orders-error-report-unavailable = A report for this repair order is unavailable.
repair-orders-error-report-terminal-pending = The deadline result is being recorded. Try printing the report again shortly.

repair-orders-object = { $type }: { $name }
repair-orders-damage-heading = Damage assessment:
repair-orders-damage-unknown = Unspecified structural damage
repair-orders-damage-event = • { $event }
repair-orders-damage-event-count = • { $event } ×{ $count }
repair-orders-error-damage = Could not prepare object damage. The offer has been retained.

repair-orders-waiver-heading = Technical exclusions
repair-orders-waiver-action = Technical exclusion: { $name }
repair-orders-waiver-cancel-action = Cancel exclusion: { $name }
repair-orders-waiver-confirm = Exclude "{ $name }" ({ $points } points) from required repairs?
    This requirement earns no points. Each active exclusion additionally deducts 1% of earned points. Final points cannot fall below zero.
    Used: { $used } / { $max } points (limit: 50% of the original order value).
    Penalty after confirmation: { $percent }%.
repair-orders-waiver-cancel-confirm = Cancel the exclusion for "{ $name }"? This requirement will become mandatory again.
    Penalty after cancellation: { $percent }%.
repair-orders-waiver-confirm-button = Confirm
repair-orders-waiver-back = Back
repair-orders-waiver-unavailable = This requirement is unavailable, already repaired, or the order is closing.
repair-orders-waiver-limit = Exclusion limit exceeded: at most 50% of the original order value.
repair-orders-waiver-unknown = Unspecified requirement
repair-orders-waiver-summary = Technical exclusions: { $count }
    Excluded: { $used } / { $max } points
    Submission penalty: { $percent }%
    Actually earned: { $raw }. After penalty: { $final }.
repair-orders-waiver-report = Technical exclusions: { $count }
    Excluded value: { $used } / { $max }
    Actually earned: { $raw }
    Technical exclusion penalty: { $percent }% ({ $penalty } points)
    Final points: { $final }
repair-orders-waiver-report-entry = • { $name }, cell ({ $x }, { $y }) — { $points } points

repair-orders-difficulty-class-1 = Routine Maintenance
repair-orders-difficulty-class-2 = Scheduled Repair
repair-orders-difficulty-class-3 = Extended Repair
repair-orders-difficulty-class-4 = Comprehensive Repair
repair-orders-difficulty-class-5 = Restorative Repair
repair-orders-difficulty-class-6 = Major Overhaul
repair-orders-difficulty-class-7 = Specialist Restoration
repair-orders-difficulty-class-8 = Emergency Restoration
repair-orders-difficulty-class-9 = Critical Restoration
repair-orders-difficulty-class-10 = Total Reconstruction

repair-order-imported-description = Restore the structure and equipment to the reference design. The work class indicates the scope of repairs.
repair-order-object-type-terminal = Orbital terminal
repair-order-object-type-evacuation-shuttle = Evacuation shuttle
repair-order-object-type-command-shuttle = Command shuttle
repair-order-object-type-prison-complex = Prison complex
repair-order-object-type-engineering-shuttle = Engineering vessel
repair-order-object-type-expedition-shuttle = Expedition vessel
repair-order-caravan-name = Restoration of Caravan
repair-order-object-name-caravan = Caravan
repair-order-launch-name = Restoration of Launch
repair-order-object-name-launch = Launch
repair-order-blizzard-name = Restoration of Blizzard
repair-order-object-name-blizzard = Blizzard
repair-order-courier-name = Restoration of Courier
repair-order-object-name-courier = Courier
repair-order-north-name = Restoration of North
repair-order-object-name-north = North
repair-order-frontier-name = Restoration of Frontier
repair-order-object-name-frontier = Frontier
repair-order-hope-name = Restoration of Hope
repair-order-object-name-hope = Hope
repair-order-dawn-name = Restoration of Dawn
repair-order-object-name-dawn = Dawn
repair-order-ark-name = Restoration of Ark
repair-order-object-name-ark = Ark
repair-order-pennant-name = Restoration of Pennant
repair-order-object-name-pennant = Pennant
repair-order-bastion-name = Restoration of Bastion
repair-order-object-name-bastion = Bastion
repair-order-eclipse-name = Restoration of Eclipse
repair-order-object-name-eclipse = Eclipse
repair-order-corsair-name = Restoration of Corsair
repair-order-object-name-corsair = Corsair
repair-order-granite-name = Restoration of Granite
repair-order-object-name-granite = Granite
repair-order-hephaestus-name = Restoration of Hephaestus
repair-order-object-name-hephaestus = Hephaestus
repair-order-apogee-name = Restoration of Apogee
repair-order-object-name-apogee = Apogee
