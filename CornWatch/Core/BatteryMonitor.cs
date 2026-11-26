using CornWatch.Models;

namespace CornWatch.Core;

// Laptop battery: charge, power source and time left come from Windows (GetSystemPowerStatus);
// charge rate and wear come from the battery's own data via LibreHardwareMonitor.
internal sealed class BatteryMonitor(HardwareSensors sensors)
{
    public BatteryInfo? Read()
    {
        var status = SystemInformation.PowerStatus;
        var lhm = sensors.ReadBattery();
        var noBattery = status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)
                        || (status.BatteryChargeStatus == BatteryChargeStatus.Unknown && lhm is null);
        if (noBattery) return null;

        var charge = status.BatteryLifePercent is >= 0f and <= 1f ? status.BatteryLifePercent * 100f : lhm?.ChargePercent ?? 0f;
        var charging = status.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging);
        var onAc = status.PowerLineStatus == PowerLineStatus.Online;

        // LibreHardwareMonitor reports capacities in mWh.
        float? design = lhm?.DesignedCapacityMWh is > 0 and var d ? d / 1000f : null;
        float? full = lhm?.FullChargeCapacityMWh is > 0 and var f ? f / 1000f : null;
        var wear = lhm?.DegradationPercent ?? (design is { } dw && full is { } fw ? (1f - fw / dw) * 100f : null);

        int? remaining = null;
        if (!onAc)
        {
            if (status.BatteryLifeRemaining > 0) remaining = status.BatteryLifeRemaining;
            else if (lhm?.RemainingSeconds is > 0 and var s) remaining = (int)s;
        }

        return new BatteryInfo
        {
            ChargePercent = Math.Clamp(charge, 0f, 100f),
            OnAcPower = onAc,
            IsCharging = charging,
            RateWatts = lhm?.RateWatts is > 0.05f and var r ? r : null,
            WearPercent = wear is { } w ? Math.Clamp(w, 0f, 100f) : null,
            DesignedCapacityWh = design,
            FullChargeCapacityWh = full,
            RemainingSeconds = remaining,
        };
    }
}
