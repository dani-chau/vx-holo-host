using System;

namespace LookingGlass {
    [Serializable]
    public enum ManualCalibrationMode {
        None = 0,
        UseCalibrationFile = 1,
        UseManualSettings = 2
    }
}
