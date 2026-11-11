// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSE-SOYUZ.TXT
using System.Threading.Tasks;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using Robust.Shared.GameObjects;
namespace Content.Shared.DeadSpace._Soyuz.Credits;

/// <summary>
/// Маркер: предмет чисто косметический. Магазин Кредитов выдаёт только такие сущности
/// </summary>
[RegisterComponent]
public sealed partial class CosmeticOnlyComponent : Component
{
}