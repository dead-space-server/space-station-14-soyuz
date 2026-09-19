// Мёртвый Космос, Союз-1, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-soyuz/master/LICENSES/LICENSE.TXT

using Content.Shared.Atmos;

namespace Content.Shared.DeadSpace._Soyuz.Atmos;

public static class SoyuzGasVisuals
{
    private static readonly Gas[] FireGases =
        [Gas.Plasma, Gas.Tritium, Gas.NitrousOxide, Gas.Frezon, Gas.Fixirium, Gas.CarbonDioxide];
    private static readonly Color[] FireColors =
        [Color.Magenta, Color.Lime, Color.Cyan, Color.LightBlue, Color.White, Color.OrangeRed];

    public static byte GetFireColor(GasMixture? air)
    {
        if (air == null || air.GetMoles(Gas.Chromatin) < 0.01f)
            return 0;
        var maximum = 0f;
        byte selected = 0;
        for (var i = 0; i < FireGases.Length; i++)
        {
            var moles = air.GetMoles(FireGases[i]);
            if (moles <= maximum)
                continue;
            maximum = moles;
            selected = (byte) (i + 1);
        }
        return selected;
    }

    public static Color FireColor(byte index) => index > 0 && index <= FireColors.Length ? FireColors[index - 1] : Color.White;

    public static Color TlecColor(byte stage, Color normal) => stage switch
    {
        2 => Color.Yellow,
        3 => Color.Orange,
        4 => Color.Red,
        5 => Color.White,
        _ => normal,
    };

    public static byte Darkness(GasMixture? air) =>
        (byte) (Math.Clamp((air?.GetMoles(Gas.Sumrak) ?? 0f) / 50f, 0f, 1f) * 230f);
}
