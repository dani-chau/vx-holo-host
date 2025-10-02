//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Assertions;
using ToolkitAPI.Device;

namespace LookingGlass {
    /// <inheritdoc cref="ToolkitAPI.Device.Calibration"/>
    [Serializable]
    public struct Calibration {
        public const int XPOS_DEFAULT = 0;
        public const int YPOS_DEFAULT = 0;
        public const string DEFAULT_NAME = "PORT";
        public const float DEFAULT_VIEWCONE = 40;

        //NOTE: (?i) is removed with (?-i)
        internal static readonly Dictionary<Regex, DeviceType> AutomaticSerialPatterns = new Dictionary<Regex, DeviceType>() {
            { new Regex("(?i)(Looking Glass - Portrait)|(PORT)|(Portrait)"),    DeviceType.Portrait },
            { new Regex("(?i)(Looking Glass - 16\")|(LKG-A)|(LKG-4K)"),         DeviceType._16in },
            { new Regex("(?i)(Looking Glass - 32\")|(LKG-B)|(LKG-8K)"),         DeviceType._32in },
            { new Regex("(?i)(Looking Glass - 65\")|(LKG-D)"),                  DeviceType._65in },
            { new Regex("(?i)(Looking Glass - 8.9\")|(LKG-2K)"),                DeviceType._8_9inLegacy },
        };

        //NOTE: Order matches LKG Toolkit's / LKG displays' visual.json order for neatness:
        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.serial"/>
        public string serial;

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.pitch"/>
        [SerializeField] private float pitch; //NOTE: This WAS the "processed pitch" value. Now we're just getting it from the device.

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.slope"/>
        [SerializeField] private float slope; //NOTE: This WAS the "processed slope" value. Now we're just getting it from the device.

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.center"/>
        public float center;

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.fringe"/>
        public float fringe;

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.viewCone"/>
        public float viewCone;

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.DPI"/>
        public float dpi;

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.screenW"/>
        public int screenWidth;

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.screenH"/>
        public int screenHeight;

        /// <inheritdoc cref="ToolkitAPI.Device.Calibration.flipImageX"/>
        public float flipImageX;
        //--- --- ---
        
        public int xpos;
        public int ypos;

        public string LKGname;

        [Tooltip("The device's native aspect ratio (screenWidth / screenHeight).\n" +
            "This does NOT necessarily match the aspect ratio for the single-view tiles in quilts that can be rendered to/from the device.\n" +
            "When these aspect ratios differ, ")]
        public float aspect;

        public bool IsValid =>  screenWidth > 0 && screenHeight > 0;

        public float DevicePitch => pitch;
        public float DeviceSlope => slope;
        public float ProcessedPitch => pitch * screenWidth / dpi * Mathf.Cos(Mathf.Atan(1 / slope));
        public float ProcessedSlope => screenHeight / (screenWidth * slope) * FlipMultiplierX;

        public float FlipMultiplierX => GetFlipMultiplierX(flipImageX);
        public bool IsSameDevice(in Calibration other) => other.serial == serial;

        public DeviceType GetDeviceType() {
            if (string.IsNullOrEmpty(serial))
                return DeviceType.Portrait;

            foreach (KeyValuePair<Regex, DeviceType> pair in AutomaticSerialPatterns)
                if (pair.Key.IsMatch(serial))
                    return pair.Value;
            
            Debug.LogError("Unknown LKG device by serial field! (serial = \"" + serial + "\")");
            return DeviceType.Portrait;
        }

        /// <summary>
        /// The actual, calculated aspect ratio of the renderer, using <see cref="screenWidth"/> and <see cref="screenHeight"/>.<br />
        /// Use this if the <see cref="aspect"/> field was modified or -1, to re-calculate the actual aspect ratio.
        /// </summary>
        public float DefaultAspect => (screenHeight == 0) ? 0 : (float) screenWidth / screenHeight;

        public Calibration(TKDisplay display) {
            screenWidth = display.calibration.screenW;
            screenHeight = display.calibration.screenH;
            viewCone = display.calibration.viewCone;
            aspect = -1;
            pitch = display.calibration.pitch;
            slope = display.calibration.slope;
            center = display.calibration.center;
            fringe = display.calibration.fringe;
            serial = display.calibration.serial;
            LKGname = display.hardwareInfo.hwid;
            flipImageX = display.calibration.flipImageX;
            dpi = display.calibration.DPI;

            xpos = display.hardwareInfo.windowCoords[0];
            ypos = display.hardwareInfo.windowCoords[1];
        }

        //WARNING: Does NOT set xy position or LKGName (hwid), since LKG Toolkit's Calibration does not contain these fields.
        public Calibration(ToolkitAPI.Device.Calibration toolkitCalibration) {
            screenWidth = toolkitCalibration.screenW;
            screenHeight = toolkitCalibration.screenH;
            viewCone = toolkitCalibration.viewCone;
            aspect = -1;
            pitch = toolkitCalibration.pitch;
            slope = toolkitCalibration.slope;
            center = toolkitCalibration.center;
            fringe = toolkitCalibration.fringe;
            serial = toolkitCalibration.serial;
            flipImageX = toolkitCalibration.flipImageX;
            dpi = toolkitCalibration.DPI;

            LKGname = "";
            xpos = 0;
            ypos = 0;
        }

        public Calibration(
            int screenWidth,
            int screenHeight,
            float viewCone,
            float aspect,
            float pitch,
            float slope,
            float center,
            float fringe,
            string serial,
            string LKGname,
            int xpos,
            int ypos,
            float flipImageX,
            float dpi
            ) {

            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            this.viewCone = viewCone;
            this.aspect = aspect;
            this.pitch = pitch;
            this.slope = slope;
            this.center = center;
            this.fringe = fringe;
            this.serial = serial;
            this.LKGname = LKGname;
            this.xpos = xpos;
            this.ypos = ypos;
            this.flipImageX = flipImageX;
            this.dpi = dpi;
        }

        public static Calibration CreateDefault() {
            Calibration c = new();

            DeviceType defaultType = DeviceType.Portrait;
            DeviceSettings settings = DeviceSettings.Get(defaultType);
            c.screenWidth = settings.screenWidth;
            c.screenHeight = settings.screenHeight;
            c.viewCone = 0;
            c.aspect = (float) c.screenWidth / c.screenHeight;
            c.pitch = 10;
            c.center = 0;
            c.fringe = 0;
            c.serial = DEFAULT_NAME;
            c.LKGname = "";
            c.xpos = XPOS_DEFAULT;
            c.ypos = YPOS_DEFAULT;
            c.flipImageX = 0;
            c.dpi = 0;

            return c;
        }

        public Calibration(int screenWidth, int screenHeight)
            : this(screenWidth, screenHeight, 0, (float) screenWidth / screenHeight,
            1, 1, 0, 0, DEFAULT_NAME, "", XPOS_DEFAULT, YPOS_DEFAULT, 0, 0
            ) { }

        /// <summary>
        /// A helper method for getting the aspect ratio of the renderer.
        /// </summary>
        /// <returns>
        /// The <see cref="aspect"/> field if it is greater than zero, or <see cref="DefaultAspect"/> otherwise.
        /// </returns>
        public float GetAspect() {
            if (aspect > 0)
                return aspect;
            return DefaultAspect;
        }

        public Calibration CopyWithCustomResolution(int xpos, int ypos, int renderWidth, int renderHeight) {
            Assert.IsTrue(typeof(Calibration).IsValueType, "The copy below assumes that "
                + nameof(Calibration) + " is a value type (struct), so the single equals operator creates a deep copy!");

            Calibration copy = this;
            copy.xpos = xpos;
            copy.ypos = ypos;
            copy.screenWidth = renderWidth;
            copy.screenHeight = renderHeight;
            copy.aspect = (renderHeight == 0) ? 0 : (float) renderWidth / renderHeight;
            return copy;
        }

        public static float GetFlipMultiplierX(float flipImageX) => (flipImageX >= 0.5f ? -1 : 1);
    }
}
