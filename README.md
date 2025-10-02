# VXHoloHost 2025 Sem 1
This is Vivi, a interactive holographic concierge that helps visitors get information about the VX Lab in an engaging way. This repository is the third iteration (version 3.0) of this application as an RMIT CSIT student capstone project.

As of writing this, there are two PCs associated with the holographic display: one DELL and one OMEN. You can find the most up to date code repository for Vivi on the OMEN PC running Windows 11, and previous versions of Vivi on the older DELL PC. We strongly recommend all future development be done on the OMEN, which is where the rest of these instructions apply to.

## Running the Vivi build
There is a folder on the Desktop called "ViviBuild" which contains the file VoiceGameLocal.exe, which is the compiled Vivi application. To run this, you first need to set up the holographic Looking Glass display.
1. Make sure the holographic display is connected the the correct PC. Also, ensure that you turn on the holographic display *AFTER* booting up the PC so that it works properly
2. Start the application "Looking Glass Bridge" and make sure it is running.
3. Start VoiceGameLocal.exe. It should be able to run and connect to the display.

## Developing Vivi in Unity
You can develop Vivi on the lab PC directly which is useful if you plan on making visual changes. The Unity project is on the Desktop in the folder called vxholohost2025. You can open it from Unity Hub.
1. Open the vxholohost Unity project and open the scene called ViviNormal
2. Do the previous steps 1 and 2 as before to set up the Looking Glass display.
3. Use the shortcut ctrl+E to connect Unity to the looking glass display, or find Looking Glass in the top tab bar of the Unity editor.
4. Run the application