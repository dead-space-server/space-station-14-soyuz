// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.Maps.NameGenerators;
using JetBrains.Annotations;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace._Soyuz.GeneralStaff;

[UsedImplicitly]
public sealed partial class GeneralStaffGenerator : StationNameGenerator
{
    private string Prefix => "Планетарная станция Генерального Штаба";
    private string[] SuffixCodes => new []{ "S" };

    public override string FormatName(string input)
    {

        // Крипсяра и рейконф отказались от суффикса и тега автора, шок
        return string.Format(input, $"{Prefix}");
    }
}
