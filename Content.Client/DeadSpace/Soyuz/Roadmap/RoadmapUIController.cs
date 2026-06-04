// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using Robust.Shared.IoC;

namespace Content.Client.DeadSpace.Soyuz.Roadmap;

public sealed class RoadmapUIController
{
    private RoadmapWindow? _window;

    public void Initialize()
    {
    }

    public void OpenRoadmap()
    {
        if (_window == null || _window.Disposed)
        {
            _window = new RoadmapWindow();
        }
        
        _window.OpenCentered();
    }
}
