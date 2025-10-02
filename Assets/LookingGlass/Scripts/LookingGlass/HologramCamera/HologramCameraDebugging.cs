using System;
using UnityEngine;

namespace LookingGlass {
    /// <summary>
    /// Contains several options, useful in the inspector, for debugging a <see cref="HologramCamera"/> component.
    /// </summary>
    [Serializable]
    public class HologramCameraDebugging : PropertyGroup {
        [NonSerialized] private bool wasShowingObjects = false;
        [NonSerialized] private float prevOnlyShowView;

        internal event Action onShowAllObjectsChanged;

        public bool ShowAllObjects {
            get { return hologramCamera.showAllObjects; }
            set {
                wasShowingObjects = hologramCamera.showAllObjects = value;
                onShowAllObjectsChanged?.Invoke();
            }
        }

        public int OnlyShowView {
            get { return hologramCamera.onlyShowView; }
            set {
                int nextValue = Mathf.Clamp(value, -1, hologramCamera.RenderSettings.numViews - 1);
                prevOnlyShowView = nextValue;
                hologramCamera.onlyShowView = nextValue;
            }
        }

        public bool OnlyRenderOneView {
            get { return hologramCamera.onlyRenderOneView; }
            set { hologramCamera.onlyRenderOneView = value; }
        }

        public ManualCalibrationMode ManualCalibrationMode {
            get { return hologramCamera.manualCalibrationMode; }
            set {
                hologramCamera.manualCalibrationMode = value;
                hologramCamera.UpdateCalibration();
            }
        }
        public TextAsset CalibrationFile {
            get { return hologramCamera.calibrationFile; }
            set {
                hologramCamera.calibrationFile = value;
                if (ManualCalibrationMode == ManualCalibrationMode.UseCalibrationFile)
                    hologramCamera.UpdateCalibration();
            }
        }
        public Calibration ManualCalibration {
            get { return hologramCamera.manualCalibration; }
            set {
                hologramCamera.manualCalibration = value;
                if (ManualCalibrationMode == ManualCalibrationMode.UseManualSettings)
                    hologramCamera.UpdateCalibration();
            }
        }

        protected override void OnInitialize() {
            hologramCamera.onRenderSettingsChanged += () => {
                OnlyShowView = OnlyShowView;
            };
        }

        protected internal override void OnValidate() {
            if (ShowAllObjects != wasShowingObjects)
                ShowAllObjects = ShowAllObjects;

            if (OnlyShowView != prevOnlyShowView)
                OnlyShowView = OnlyShowView;
        }
    }
}
