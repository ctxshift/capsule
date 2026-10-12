using System;
namespace PortableUi
{
    public interface IUiGuest : IDisposable
    {
        UiSession Session { get; }
        Action<string> InitializationTrace { get; set; }
        bool Initialize(out string error);
        bool Update(out string error);
        bool SetViewport(int width, int height, out string error);
    }
}
