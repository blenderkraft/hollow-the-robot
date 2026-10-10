# Hollow the Robot — MODLOG

Goal: the Knight (Hollow Knight) as Haiku's playable character in **Haiku, the Robot**, using Haiku's physics,
with Hollow Knight abilities layered on.

## Versions
- Haiku, the Robot (Steam 1231880): Unity 2019.4.36f1 Mono, Windows build via Proton. BepInEx 5.4.23.5 already
  installed (from ~/Dev/haiku-cheats). Launch option `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- Hollow Knight (Steam 367520): **Unity 6000.0.61f1** (2025 re-release build), Mono. Not modified — only read.
- Toolchain: ~/.dotnet (SDK 8.0.425), ilspycmd in ~/.dotnet/tools, uv + UnityPy 1.25.4 + TypeTreeGeneratorAPI.
- Haiku decompile: ~/haiku-decomp (outside repo).

## Route
- Haiku side: BepInEx plugin + HarmonyX (src/HollowTheRobot). Haiku's PlayerScript keeps physics; its Animator
  keeps running invisibly (it fires sword-hit/heal/footstep animation events); we hide its SpriteRenderer
  (alpha 0) and draw the Knight on a child SpriteRenderer.
- HK side: tools/extract_knight.py reads the user's HK install at install time ("bring your own game files").
  Nothing from HK is committed or redistributed.

## HK asset facts
- Knight tk2dSpriteCollectionData "Knight" (891 sprites, one 4096² BC7 atlas "atlas0") in resources.assets.
- Knight tk2dSpriteAnimation (214 clips) in resources.assets; found by clip "Wall Slide".
- Type trees are stripped: MonoScript lookup via globalgamemanagers.assets (m_FileID 2), then
  TypeTreeGenerator on Managed/ (TeamCherry.TK2D.dll holds tk2d classes).
- tk2d sprite: positions[0]=min, [3]=max in units at texel 1/64; `flipped`=1 means rotated in atlas
  (pos x -> atlas v, pos y -> atlas u) → transpose.
- Effect offsets read from the Knight prefab transforms (Attacks/Slash etc.), see KnightController.

## Haiku facts
- PPU 16, pixel-perfect camera (Unity.2D.PixelPerfect). Haiku body ~22 px tall. Extractor scale 0.19 → Knight
  ~25 px. HK unit → Haiku unit = 64*0.19/16 = 0.76.
- Art faces left at localScale.x = +1 (`side`=1), same as HK.
- Heal = hold "Heal"; healing applied by animation events (PlayerHealth.AddHealingCount).
- Damage: IDamageable.TakeDamage(int dmg, int side, Vector2 playerPos); bombs use INonSwordDmg.

## Input
- Rewired_Core won't decompile cleanly in ilspycmd; instead a transpiler swaps every Player.GetButton*/GetAxis*
  call in PlayerScript.Update for InputHooks wrappers (tap Heal = cast, hold = focus; lock during abilities).

## Restore
- Saves backup: ~/.universal-modder/backups/haiku-saves-htr/ (`um backup restore`).
- Uninstall: delete BepInEx/plugins/HollowTheRobot.

## Verified in game (2026-10-09, autopilot run, screenshots read)
- Knight renders at ~25 px, correct facing, runs/jumps/airborne/land, slash + up-slash arcs, Vengeful Spirit
  (face leads), Howling Wraiths, Desolate Dive (fall + blast), Focus (+additive glow), Great Slash, Cyclone
  (looping whoosh), Shade Cloak visuals on blink, Monarch Wings double jump. No exceptions in the log.
- NOT yet verified: damage to enemies and Soul gain from real enemies (start room has none).

## Gotchas
1. Haiku's build strips Unity's ImageConversion (Texture2D.LoadImage → NRE) and JsonUtility (FromJson → null),
   and the PNG encoder. Use LoadRawTextureData, a text format, and ReadPixels → raw dump.
2. Coroutines resume after Update: a fake "button down" set there must survive until the next frame's Update.
3. Steam ignores -applaunch while a previous instance is still running — check the PID before relaunching.
4. Fx.Spawn auto-destroys looping clips after one cycle unless `persistent`.
5. Some HK effects (Focus Effect) are additive in HK; the extractor turns them into white-with-luminance-alpha.
6. Copying 001.haiku to 003.haiku opens the game-mode picker in SelectSaveFile; the autopilot sets
   GameManager.saveFilePath and calls StartNewGame("normal") directly.
7. Hyprland on this machine uses the Lua config: `hyprctl keyword windowrulev2` is rejected.
