using System;
using System.Linq;

namespace AudioFollow
{
    public sealed class DirectionResult
    {
        public AudioSnapshot Snapshot;
        public string Error;
        public bool Changed;
    }

    public static class RouteApplier
    {
        public static DirectionResult Apply(EndpointTarget target, AudioSnapshot snapshot, int flow,
            Action<string> set, Func<AudioSnapshot> read)
        {
            var result = new DirectionResult { Snapshot = snapshot };
            if (target.KeepCurrent) return result;
            Endpoint desired = target.Resolve(snapshot, flow);
            if (desired == null) { result.Error = "指定设备不可用，保留当前设备。"; return result; }
            if (snapshot.Defaults.All(id => id == desired.Id)) return result;
            try
            {
                set(desired.Id);
                AudioSnapshot after = read();
                if (!after.Defaults.All(id => id == desired.Id)) throw new InvalidOperationException("三种默认角色回读不一致。");
                result.Snapshot = after; result.Changed = true;
            }
            catch (Exception ex) { result.Error = "切换失败：" + ex.Message; }
            return result;
        }
    }
}
