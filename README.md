

---

# **Vector 2 Level Editor**

A fork of **Sonamenil’s Vector 2 Level Editor** and the **Vector 2 Unity project**. This version features a full-fledged UI, the ability to run as a separate application, and support for loading custom textures through its own runtime.

This tool was created with the goal of making **Vector 2 modding easier** by providing plug-and-play systems for creating custom levels and remove the need to install unity.

It is recommended that you have some knowledge of the **Vectorier Editor** before trying this tool. If you don’t, you can watch my tutorial series here:

YouTube

www.youtube.com/@Enderdude290

---

# **Setup**

To load and play custom rooms, you must install **my modified version of Vector 2**, which can be found in the **Releases tab** or on its own GitHub page. Link is here: [https://github.com/Ghost29012/Vector-2-/releases/tag/Release](https://github.com/Ghost29012/Vector-2-/releases/tag/Release)

Once the game runs at least once and you enter a level, the game will create two folders inside your Nekki directory:

* custom\_rooms  
* custom\_textures

MacOS directory:

/Users/\<yourUser\>/Library/Application Support/com.Nekki.Vector-2

or /Users/\<yourUser\>/Library/Application Support/Nekki/vector 2 in case the first directory doesn't work, its usually because of you using the Unity project/

Windows directory:

C:\\Users\\\<YourUsername\>\\AppData\\LocalLow\\Nekki\\Vector 2  
---

# **Connecting the Editor**

Inside the level editor:

1. Click **Script Manager**  
2. A directory section will be visible  
3. Paste the directory for your **custom\_rooms** folder

Important notes:

* ONLY the **custom\_rooms folder path** should be entered  
* Make sure there are **no spaces at the end of the directory**  
* A zone2 folder may appear for organization

Also note:

Do **NOT place textures inside subfolders** in custom\_texture.

Textures inside subfolders will **not load in-game**.

---

# **Custom Textures**

When creating a custom texture(s):

1. Place the texture inside the game’s custom\_textures folder  
2. Place the same texture inside the **Level Editor StreamingAssets folder for the level editor**

Both locations are required for the texture to work correctly.

### **Finding StreamingAssets for the level editor**

MacOS:

1. Right-click the application  
2. Click **Show Package Contents**  
3. Navigate to:

Resources → Data → StreamingAssets

Windows:

The StreamingAssets folder will be inside the Vector 2 Level Editor_Data folder.

After adding a new texture, **click the Refresh button inside the editor** so the texture appears.

---

# **Important Things To Note**

Your level **MUST include both of these prefabs**:

* In.prefab  
* Out.prefab

If either one is missing, **the game will not load the level**.

The editor includes a **rotation system**, but it is strongly recommended **not to use it**. Vector 2 does not handle rotated objects very well.

If you are using background objects or tagging objects as background elements, it is recommended to test the level in the **Lab environment**, since the Maintenance Area handles backgrounds differently. Also, if you are trying to run the level editor in the Unity editor and notice errors, do not mind them; they will not affect the build, these errors are windows only.

---

# **Loading Levels In Game**

Open the console using the \~ key and type:

play \<your level name\>

Example:

play maptest1  
---

# **Chaining Multiple Levels**

You can load multiple levels in sequence by **separating them with spaces**.

Example:

play room1 room2 room3

Each level name must be separated by a **space**; the command will not work correctly.

---

# **Skipping Game Progression For Testing**

When testing levels, it is recommended to skip the normal game flow by typing:

z2 11

You can disable debug information through the **settings menu**.

---

# **Important Behaviour Note**

If you click **Play normally**, the game may generate **custom levels together with normal (vanilla) levels**.

If you load levels using the **console command instead**, the game will load your custom level directly without mixing it with the normal level generation. If you would like to play a vanilla playthrough, consider removing the custom rooms from the folder. 

---

# **Unity Editor Users**

If you are creating levels **directly inside the Unity editor instead of the app**, you must:

1. Click **Play**  
2. Click **Build Map Vec 2**

This step is required so the level properly compiles for the game. Also the scene to use in the editor is empty_scene, it has the script manager and camera that you will need.

---

# **Additional Notes**

The editor defaults to **maptest1** every time it starts.

You can change the level name inside **Script Manager**.

Even though Script Manager says **override**, it does **not actually override anything unless it has the same name as the level in the folder**.

It simply creates a new custom level.

The game may generate custom levels alongside normal ones if you use the Play button normally. If you want to do a vanilla playthrough, **remove custom rooms temporarily**.

You can also load:

* Maintenance Area textures in the Lab  
* Lab textures in the Maintenance Area

---

# **Tricks And Features**

The prefab used for tricks is named:

stunt

You can edit it using the **Raw XML button**.

By default, the game will not have all tricks unlocked. To unlock tricks for testing, open the console and run:

bp 500 add

(or any amount you prefer)

Then run:

bpo \<same amount\>

Example:

bpo 500

The game may freeze briefly depending on the number used.

This is normal.

Once the game unfreezes, run:

lvlup

You will now have access to tricks.

You can also view trick names inside the game, which makes setting up stunt systems easier.

For **wall jumps**, you will need:

* walljump prefab  
* two WallrunFromFail objects

There is also an **Add Components tab** in the editor. And you can parent objects by dragging them in the hierarchy


**Trick Names (you can copy and paste these into raw XML)**

Here is a list of all the tricks:
BoomBoomSh
DiveDown
DiveRoll
DoubleBack
DoubleJumpRoll
DoubleSpinToRoll
DoubleSpinVault
FlyingArrow
FrontFlipTwoLegs
FrontflipLegsUp
HandspringToRoll
JumpDownRoll
JumpSpinVault
KingKongJumpoff
KingKongToBend
LongCircle
LongJumpToBarrel
MonkeyToBomb
RollToStraightLegsFlip
SideFlip
SlowSpin
Spin
SpinBicycle
SpinVault
SplitOne
TurnVault
Underbar
WallBackRoll
WallCling
WallHop
AboveCar
AirBomb
AirSpin
BackFlip
BarJump
BarJumpSaltoless
BarrelVault
BoomBoomSh
CoolSwing
DashToFrontflip
DashVault
DiveDown
DiveRoll
DoubleBack
DoubleJumpRoll
DoubleKong
DoubleSpinToRoll
DoubleSpinVault
FlyingArrow
FrontFlipTwoLegs
FrontflipLegsUp
GateVault
HandSpring
HandspringToRoll
JumpDownRoll
JumpObstacle
JumpSpinVault
JumpTumble
JumpWheel
KingKongJumpOff
KingKongToBend
LongCircle
LongJumpToBarrel
MonkeyToBackflip
MonkeyToBomb
MonkeyVault
ObstacleFrontflip
RailFlipVault
ReverseVault
RocketVault
RollForward
RollToStraightlegsFlip
ScrewDriver
SideBomb
SideFlip
SlowSpin
Spin
SpinBicycle
SpinVault
SpinningVault
SplitOne
Swallow
ThiefVault
TripleHit
TripleSwing
TripleTrickToSwallow
TurnVault
Underbar
VertVault
WallBackRoll
WallCling
WallHop
Webster
WebsterWithSalto




---

# **Controls**

Editor camera controls:

Middle mouse button → move camera

Scroll wheel → zoom

Object controls:

Left click → move objects

Selection:

Shift \+ click → multi select 

Control / Command \+ click → select multiple objects

---

# **Bug Reporting**

If you would like to assist with bug reporting, consider downloading the **development build** and sharing any console errors. This helps with identifying and fixing issues more directly.

---

# **Copying Folder Paths**

MacOS:

Hold **Option**, right-click the folder, and select **Copy as Pathname**.

Sometimes the pasted path will include ' characters at the beginning and end.

Remove those characters before using the path.

Windows:

Click the address bar in File Explorer and copy the path directly.

---

# **Future Additions**

Planned improvements include:

* Importing XML levels back into the editor  
* Maintenance Area traps  
* SWARM trigger support
* Patched with the Unity patching tool
* Remove the made with Unity start-up
* Prefab Preveiwing like png images
* Possible dedicated custom background folder for more detailed custom backgrounds (BIG maybe. If its not added in the next 4 major releases then it has been abandoned or ask me about it)

---

Also, I own NOTHING all of this is the rightful property of Nekki. 




# **Need Help?**

You can contact me through:

YouTube

www.youtube.com/@Enderdude290

Discord

enderdude290

# Additional Links:

My modified vector 2 Project with custom room and custom texture loading (NEEDED\!): (https://github.com/Ghost29012/Vector-2-/releases/tag/Release)   ](https://github.com/Ghost29012/Vector-2-mod-support-release/releases/tag/Release)
Sonamenils original project:[https://github.com/sonamenil/Vectorier-Editor-Vector2?tab=readme-ov-file](https://github.com/sonamenil/Vectorier-Editor-Vector2?tab=readme-ov-file)  
Sonamenils Vector 2 Unity project: [https://github.com/sonamenil/Vector2-UnityProject](https://github.com/sonamenil/Vector2-UnityProject)

