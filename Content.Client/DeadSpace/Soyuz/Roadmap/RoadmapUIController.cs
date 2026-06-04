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
