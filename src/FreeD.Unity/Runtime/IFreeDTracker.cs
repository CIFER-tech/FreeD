using CIFER.tech.FreeD.Core;

namespace CIFER.tech.FreeD.Unity
{
    public interface IFreeDTracker
    {
        int CameraId { get; }

        void Apply(in FreeDPacket packet, FreeDField appliedFields);
    }
}
