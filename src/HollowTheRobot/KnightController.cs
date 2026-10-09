using System.Collections.Generic;
using HarmonyLib;
using Rewired;
using UnityEngine;

namespace HollowTheRobot
{
    /// <summary>
    /// Lives on Haiku's player object. Haiku's PlayerScript keeps running the physics and its Animator keeps firing
    /// gameplay events (sword hits, heals, footsteps); this component hides Haiku's sprite, draws the Knight in his
    /// place, and adds Hollow Knight's abilities on top.
    /// </summary>
    public class KnightController : MonoBehaviour
    {
        public static KnightController Instance;
        public static int SortingLayer;

        private enum State { None, AirDash, Fireball, Wraiths, DiveAntic, DiveFall, DiveLand, GreatSlash, DashSlash, Cyclone }

        private PlayerScript ps;
        private Rigidbody2D rb;
        private Collision coll;
        private AnimationScript hanim;
        private SpriteRenderer haikuSr;
        private SpriteRenderer swordSr;
        private Player rw;

        private GameObject knight;
        private SpriteRenderer ksr;
        private KAnim kanim;
        private Transform fxRoot;
        private float footLocalY;
        private bool showKnight = true;

        // one-shot body animation that wins over the locomotion state
        private string overrideClip;
        private float overrideTimer;
        private string queuedClip;

        private State state;
        private float stateTime;
        private float savedGravity;
        private bool gravitySaved;
        private bool setDashing;
        private HashSet<object> hitSet = new HashSet<object>();
        private float nextTick;
        private KAnim stateFx;

        private bool airDashUsed;
        private float lastDashTime = -10f;
        private bool slashAlt;
        private float attackHeld;
        private bool charged;
        private KAnim focusFx;
        private float hurtFlicker;

        private bool wasGrounded = true;
        private bool wasDoubleJumped;
        private bool wasWallJumped;
        private float airTime;
        private bool restartAirborne;

        private static readonly AccessTools.FieldRef<PlayerScript, float> BlinkCost =
            AccessTools.FieldRefAccess<PlayerScript, float>("blinkCost");

        private float U => KnightAssets.HkUnit;
        /// <summary>World facing: +1 = left (Hollow Knight's and Haiku's default art direction).</summary>
        private int Facing => ps.side;
        /// <summary>World x direction the Knight is looking at.</summary>
        private float Forward => -ps.side;

        private void Start()
        {
            Instance = this;
            ps = GetComponent<PlayerScript>();
            rb = ps.rb != null ? ps.rb : GetComponent<Rigidbody2D>();
            coll = GetComponent<Collision>();
            hanim = GetComponentInChildren<AnimationScript>();
            haikuSr = hanim != null ? hanim.GetComponent<SpriteRenderer>() : null;
            if (hanim != null && hanim.attack != null && hanim.attack.attackVisual != null)
                swordSr = hanim.attack.attackVisual.GetComponent<SpriteRenderer>();
            rw = ReInput.players.GetPlayer(0);

            knight = new GameObject("HTR Knight");
            knight.transform.SetParent(transform, false);
            ksr = knight.AddComponent<SpriteRenderer>();
            kanim = knight.AddComponent<KAnim>();
            kanim.sr = ksr;
            kanim.Play("Idle");
            fxRoot = new GameObject("HTR Knight Fx").transform;
            fxRoot.SetParent(knight.transform, false);

            var box = ps.boxColl;
            footLocalY = box != null ? box.offset.y - box.size.y / 2f : -0.6f;
            knight.transform.localPosition = new Vector3(0f, footLocalY + KnightAssets.FootOffset(), 0f);

            if (GetComponent<SoulHud>() == null) gameObject.AddComponent<SoulHud>();
            Plugin.Log.LogInfo($"Knight attached to player (feet at local y {footLocalY:F2})");
        }

        private void OnDisable()
        {
            EndState();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            InputHooks.Locked = false;
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (Plugin.DebugKeys.Value)
            {
                if (Input.GetKeyDown(KeyCode.F5)) Soul.Add(Soul.Max);
                if (Input.GetKeyDown(KeyCode.F6)) showKnight = !showKnight;
            }

            if (ps.isDead)
            {
                if (state != State.None) EndState();
                return;
            }
            if (Time.timeScale == 0f) return;

            if (coll.onGround || ps.isSliding) airDashUsed = false;
            if (ps.isDashing && !setDashing) lastDashTime = Time.time;

            if (state == State.None)
            {
                TryAirDash();
                UpdateNailCharge();
            }
            else
            {
                stateTime += Time.deltaTime;
                UpdateState();
            }
        }

        private bool CanAct =>
            state == State.None && !ps.isDead && !ps.disableControls && !ps.isRolling && !ps.isHealing
            && !(ps.grappleRope != null && ps.grappleRope.isGrappling)
            && !(CameraBehavior.instance != null && CameraBehavior.instance.isTransitioning);

        // ------------------------------------------------------------------ Mothwing Cloak

        private void TryAirDash()
        {
            if (!Plugin.AirDash.Value || airDashUsed || coll.onGround || ps.isSliding || !CanAct) return;
            var gm = GameManager.instance;
            if (!gm.canDash || gm.canTeleport) return; // with the teleport upgrade Haiku's own blink works in mid-air
            if (!rw.GetButtonDown("Dash") || ps.isDashing) return;
            if (ManaManager.instance != null && ManaManager.instance.cooldown)
            {
                ManaManager.instance.OnCooldownFeedback();
                return;
            }
            airDashUsed = true;
            ManaManager.instance?.AddHeat(BlinkCost(ps));
            ps.InvulnerableFor(ps.dashTime + 0.1f);
            ps.isDashing = true;
            setDashing = true;
            lastDashTime = Time.time;
            Combat.Sfx(ps.slideSFXPath);
            ps.dashParticle?.Play();
            Fx.Spawn("Dash Effect", knight.transform.position, Facing, Vector2.one, null, ksr.sortingOrder - 1, SortingLayer);
            Enter(State.AirDash, lockInput: true, hover: true);
        }

        // ------------------------------------------------------------------ Nail Arts

        private void UpdateNailCharge()
        {
            bool held = Plugin.NailArts.Value && rw.GetButton("Attack") && GameManager.instance.canAttack && CanAct
                        && !InputHooks.Locked;
            if (held)
            {
                attackHeld += Time.deltaTime;
                if (!charged && attackHeld >= Plugin.NailArtChargeTime.Value)
                {
                    charged = true;
                    Fx.Spawn("NA Charged Effect", knight.transform.position, Facing, Vector2.one, knight.transform, ksr.sortingOrder + 1, SortingLayer);
                    Combat.Sfx("event:/CriticalHit");
                }
                return;
            }
            if (charged && CanAct)
            {
                float v = rw.GetAxisRaw("Move Vertical");
                if (Mathf.Abs(v) > 0.5f) StartCyclone();
                else if (ps.isDashing || Time.time - lastDashTime < 0.2f) StartDashSlash();
                else StartGreatSlash();
            }
            attackHeld = 0f;
            charged = false;
        }

        private void StartGreatSlash()
        {
            Enter(State.GreatSlash, lockInput: true, hover: true);
            PlayBody("NA Big Slash");
            AttachFx("NA Big Slash Effect", new Vector2(-0.68f, 0f), new Vector2(1.316f, 1.316f));
            Combat.Sfx("event:/Attack V.2");
        }

        private void StartDashSlash()
        {
            Enter(State.DashSlash, lockInput: true, hover: true);
            PlayBody("NA Dash Slash");
            AttachFx("NA Dash Slash Effect", new Vector2(-2.69f, -0.24f), new Vector2(1.316f, 1.316f));
            ps.InvulnerableFor(0.3f);
            Combat.Sfx("event:/Attack V.2");
        }

        private void StartCyclone()
        {
            Enter(State.Cyclone, lockInput: true, hover: true);
            PlayBody("NA Cyclone Start", "NA Cyclone");
            stateFx = AttachFx("Cyclone Effect", new Vector2(0f, -0.363f), new Vector2(1.44f, 1.911f));
            if (stateFx != null) stateFx.destroyWhenDone = false;
            nextTick = 0.1f;
        }

        // ------------------------------------------------------------------ Spells

        /// <summary>Called by InputHooks when Heal is tapped.</summary>
        public void TryCast()
        {
            if (!Plugin.Spells.Value || !CanAct || InputHooks.Locked || Time.timeScale == 0f) return;
            float v = rw.GetAxisRaw("Move Vertical");
            bool up = v > 0.5f || rw.GetButton("Attack Up");
            bool down = v < -0.5f || rw.GetButton("Attack Down");
            if (!Soul.Spend(Plugin.SpellCost.Value))
            {
                ManaManager.instance?.OnCooldownFeedback();
                return;
            }
            if (up) StartWraiths();
            else if (down) StartDive();
            else StartFireball();
        }

        private void StartFireball()
        {
            Enter(State.Fireball, lockInput: true, hover: true);
            PlayBody("Fireball1 Cast");
            var pos = knight.transform.position + new Vector3(Forward * 0.7f, 0.1f, 0f);
            Fx.Spawn("Spell/Blast", pos, Facing, Vector2.one, null, ksr.sortingOrder + 2, SortingLayer);
            var go = new GameObject("HTR Vengeful Spirit");
            go.transform.position = pos;
            var fb = go.AddComponent<VengefulSpirit>();
            fb.Init(Forward, coll.groundLayer, ksr.sortingOrder + 2);
            Combat.Sfx(ps.bombProjectileSFXPath);
        }

        private void StartWraiths()
        {
            Enter(State.Wraiths, lockInput: true, hover: true);
            PlayBody("Scream Start", "Scream");
            var a = Fx.Spawn("Spell/Scream1", knight.transform.position + new Vector3(0f, 2.59f * U, 0f), Facing,
                new Vector2(1.4f, 1.4f), transform, ksr.sortingOrder + 2, SortingLayer);
            Fx.Spawn("Spell/Scream1 Base", knight.transform.position, Facing, new Vector2(1.4f, 1.4f), transform, ksr.sortingOrder + 1, SortingLayer);
            ps.InvulnerableFor(0.4f);
            nextTick = 0.08f;
            Combat.Sfx(ps.bombRadiusSFXPath);
            Combat.Shake(0.4f, 0.02f);
        }

        private void StartDive()
        {
            Enter(State.DiveAntic, lockInput: true, hover: true);
            PlayBody("Quake Antic");
            ps.InvulnerableFor(3f);
            Fx.Spawn("Spell/Quake Charge", knight.transform.position, Facing, Vector2.one, knight.transform, ksr.sortingOrder + 1, SortingLayer);
        }

        // ------------------------------------------------------------------ state machine

        private void Enter(State s, bool lockInput, bool hover)
        {
            EndState();
            state = s;
            stateTime = 0f;
            hitSet.Clear();
            InputHooks.Locked = lockInput;
            if (hover)
            {
                savedGravity = rb.gravityScale;
                gravitySaved = true;
                rb.gravityScale = 0f;
            }
        }

        private void EndState()
        {
            if (state == State.None) return;
            state = State.None;
            InputHooks.Locked = false;
            if (gravitySaved && rb != null) rb.gravityScale = savedGravity;
            gravitySaved = false;
            if (setDashing && ps != null) ps.isDashing = false;
            setDashing = false;
            if (stateFx != null) Destroy(stateFx.gameObject);
            stateFx = null;
            overrideClip = null;
            queuedClip = null;
            if (ps != null && ps.isInvunerable && ps.isInvunerableTimer > 0.5f) ps.isInvunerableTimer = 0.15f;
        }

        private void UpdateState()
        {
            Vector2 p = knight.transform.position;
            switch (state)
            {
                case State.AirDash:
                    if (stateTime >= 0.24f) EndState();
                    break;

                case State.Fireball:
                    if (stateTime >= 0.3f) EndState();
                    break;

                case State.Wraiths:
                    if (stateTime >= nextTick && nextTick < 0.4f)
                    {
                        Combat.HitBox(p + new Vector2(0f, 2.1f * U), new Vector2(3.2f, 4.4f * U),
                            Combat.Scaled(Plugin.SpellDamage.Value * 0.87f));
                        nextTick += 0.12f;
                    }
                    if (stateTime >= 0.6f)
                    {
                        PlayBody("Scream End");
                        EndState();
                    }
                    break;

                case State.DiveAntic:
                    if (stateTime >= 0.18f)
                    {
                        state = State.DiveFall;
                        stateTime = 0f;
                        PlayBody("Quake Fall");
                        stateFx = AttachFx("Spell/Quake Dash", Vector2.zero, Vector2.one);
                        if (stateFx != null) stateFx.destroyWhenDone = false;
                    }
                    break;

                case State.DiveFall:
                    Combat.HitCircle(p, 0.8f, Combat.Scaled(Plugin.SpellDamage.Value * 0.5f), hitSet);
                    if (coll.onGround || stateTime > 2.5f)
                    {
                        if (stateFx != null) Destroy(stateFx.gameObject);
                        stateFx = null;
                        state = State.DiveLand;
                        stateTime = 0f;
                        PlayBody("Quake Land");
                        Vector2 feet = (Vector2)transform.position + new Vector2(0f, footLocalY);
                        Fx.Spawn("Spell/Quake Blast", (Vector3)(feet + new Vector2(0.03f * U, 0.822f * U)), Facing,
                            new Vector2(1.386f, 1.386f), null, ksr.sortingOrder + 2, SortingLayer);
                        hitSet.Clear();
                        Combat.HitBox(feet + new Vector2(0f, 0.9f), new Vector2(6.5f * U, 2.4f),
                            Combat.Scaled(Plugin.SpellDamage.Value), hitSet);
                        Combat.Shake(0.35f, 0.06f);
                        Combat.Sfx(ps.bombRadiusSFXPath);
                    }
                    break;

                case State.DiveLand:
                    if (stateTime >= 0.3f) EndState();
                    break;

                case State.GreatSlash:
                    if (stateTime >= 0.08f && hitSet.Count == 0 && nextTick == 0f)
                    {
                        nextTick = 1f;
                        Combat.HitBox(p + new Vector2(Forward * 2.0f, 0.2f), new Vector2(4.0f, 1.8f),
                            Combat.Scaled(Plugin.NailArtDamage.Value), hitSet);
                        Combat.Shake(0.15f, 0.02f);
                    }
                    if (stateTime >= 0.4f) { nextTick = 0f; EndState(); }
                    break;

                case State.DashSlash:
                    if (stateTime >= 0.12f && nextTick == 0f)
                    {
                        nextTick = 1f;
                        Combat.HitBox(p + new Vector2(Forward * 1.4f, 0f), new Vector2(4.2f, 1.6f),
                            Combat.Scaled(Plugin.NailArtDamage.Value), hitSet);
                    }
                    if (stateTime >= 0.35f) { nextTick = 0f; EndState(); }
                    break;

                case State.Cyclone:
                    if (stateTime >= nextTick && stateTime < 1.1f)
                    {
                        Combat.HitCircle(p + new Vector2(0f, 0.1f), 2.1f, Combat.SwordDamage);
                        nextTick += 0.18f;
                    }
                    if (stateTime >= 1.2f)
                    {
                        PlayBody("NA Cyclone End");
                        nextTick = 0f;
                        EndState();
                    }
                    break;
            }
        }

        /// <summary>Called after PlayerScript/BetterJumping FixedUpdate so the Knight's moves win.</summary>
        public void ApplyVelocityOverride()
        {
            if (rb == null || state == State.None || rb.bodyType != RigidbodyType2D.Dynamic) return;
            switch (state)
            {
                case State.AirDash:
                    rb.velocity = new Vector2(Forward * 16f, 0f);
                    break;
                case State.Fireball:
                    rb.velocity = stateTime < 0.12f ? new Vector2(-Forward * 3f, 0f) : Vector2.zero;
                    break;
                case State.Wraiths:
                case State.DiveAntic:
                case State.DiveLand:
                case State.GreatSlash:
                    rb.velocity = Vector2.zero;
                    break;
                case State.DiveFall:
                    rb.velocity = new Vector2(0f, -30f);
                    break;
                case State.DashSlash:
                    rb.velocity = stateTime < 0.18f ? new Vector2(Forward * 22f, 0f) : Vector2.zero;
                    break;
                case State.Cyclone:
                    rb.velocity = new Vector2(rw.GetAxisRaw("Move Horizontal") * 4.5f, Mathf.Max(rb.velocity.y, -1.5f));
                    break;
            }
        }

        // ------------------------------------------------------------------ hooks from Haiku

        public void OnSlash(int dir)
        {
            if (state != State.None) return;
            charged = false;
            if (dir > 0)
            {
                PlayBody("UpSlash");
                AttachFx("UpSlashEffect", new Vector2(0f, 0.69f), new Vector2(1.15f, 1.4f));
            }
            else if (dir < 0)
            {
                PlayBody("DownSlash");
                AttachFx("DownSlashEffect", new Vector2(0.16f, -1.59f), new Vector2(1.125f, 1.28f));
            }
            else if (ps.isSliding)
            {
                PlayBody("Wall Slash");
                AttachFx("SlashEffect", new Vector2(0.04f, -0.41f), new Vector2(-1.62f, 1.645f));
            }
            else
            {
                slashAlt = !slashAlt;
                if (slashAlt)
                {
                    PlayBody("Slash");
                    AttachFx("SlashEffect", new Vector2(-0.01f, -0.41f), new Vector2(1.62f, 1.645f));
                }
                else
                {
                    PlayBody("SlashAlt");
                    AttachFx("SlashEffectAlt", new Vector2(0.08f, -0.436f), new Vector2(1.257f, 1.422f));
                }
            }
        }

        public void OnJump()
        {
            restartAirborne = true;
        }

        public void OnHurt()
        {
            if (state != State.None) EndState();
            PlayBody("Recoil");
            hurtFlicker = 1.0f;
        }

        public void OnHealed()
        {
            if (ps != null && ps.isHealing)
            {
                PlayBody("Focus Get", "Focus");
                Fx.Spawn("Focus/Burst Effect", knight.transform.position, Facing, Vector2.one, knight.transform, ksr.sortingOrder + 1, SortingLayer);
            }
        }

        // ------------------------------------------------------------------ visuals

        private void PlayBody(string clip, string then = null)
        {
            var c = KnightAssets.Clip(clip);
            if (c == null) return;
            overrideClip = clip;
            overrideTimer = c.frames.Length / Mathf.Max(1f, c.fps);
            queuedClip = then;
            kanim.Play(clip, true);
        }

        private KAnim AttachFx(string clip, Vector2 hkOffset, Vector2 hkScale)
        {
            var a = Fx.Spawn(clip, Vector3.zero, 1f, hkScale, fxRoot, ksr.sortingOrder + 1, SortingLayer);
            if (a != null) a.transform.localPosition = new Vector3(hkOffset.x * U, hkOffset.y * U, 0f);
            return a;
        }

        private void LateUpdate()
        {
            if (haikuSr == null) return;
            bool rolling = ps.isRolling;
            bool visible = showKnight && !rolling;

            // Hide Haiku (but keep his Animator running for gameplay events). The ball form stays Haiku's.
            var hc = haikuSr.color;
            haikuSr.color = new Color(hc.r, hc.g, hc.b, visible ? 0f : 1f);
            if (swordSr != null)
            {
                var sc = swordSr.color;
                swordSr.color = new Color(sc.r, sc.g, sc.b, visible ? 0f : 1f);
            }

            ksr.enabled = visible && haikuSr.enabled;
            fxRoot.gameObject.SetActive(ksr.enabled);
            SortingLayer = haikuSr.sortingLayerID;
            ksr.sortingLayerID = haikuSr.sortingLayerID;
            ksr.sortingOrder = haikuSr.sortingOrder;
            ksr.sharedMaterial = haikuSr.sharedMaterial;

            // Colour: Haiku's tint, plus HK-style hurt flicker and Nail Art charge glow.
            Color col = new Color(hc.r, hc.g, hc.b, 1f);
            if (hurtFlicker > 0f)
            {
                hurtFlicker -= Time.deltaTime;
                if (ps.isInvunerable && Mathf.Repeat(Time.time, 0.12f) < 0.05f) col.a = 0.35f;
            }
            if (attackHeld > 0.3f && state == State.None)
            {
                float t = charged ? 0.5f + 0.5f * Mathf.Sin(Time.time * 30f) : Mathf.Clamp01((attackHeld - 0.3f) / Plugin.NailArtChargeTime.Value) * 0.6f;
                col = Color.Lerp(col, new Color(1f, 1f, 1f, col.a), t);
                col.r = col.g = col.b = Mathf.Max(col.r, 0.6f + 0.4f * t);
            }
            ksr.color = col;

            // Facing: the Knight clings to walls facing away from them, like in Hollow Knight.
            int facing = Facing;
            if (ps.isSliding && state == State.None) facing = coll.onRightWall ? 1 : -1;
            float parentX = Mathf.Sign(transform.localScale.x);
            knight.transform.localScale = new Vector3(facing * parentX, 1f, 1f);

            UpdateFocusFx();
            UpdateBodyClip();
        }

        private void UpdateFocusFx()
        {
            bool focusing = ps.isHealing && ksr.enabled;
            if (focusing && focusFx == null)
            {
                focusFx = Fx.Spawn("Focus/Focus Effect", knight.transform.position, 1f, Vector2.one, knight.transform, ksr.sortingOrder + 1, SortingLayer);
                if (focusFx != null) focusFx.destroyWhenDone = false;
            }
            else if (!focusing && focusFx != null)
            {
                Destroy(focusFx.gameObject);
                focusFx = null;
            }
        }

        private void UpdateBodyClip()
        {
            bool grounded = coll.onGround;
            float vy = rb.velocity.y;

            // Edge-triggered one-shots
            if (ps.hasDoubleJumped && !wasDoubleJumped && !grounded)
            {
                PlayBody("Double Jump");
                Fx.Spawn("Double Jump Wings 2", knight.transform.position + new Vector3(0.22f * U * Facing, -0.064f * U, 0f), Facing,
                    Vector2.one, null, ksr.sortingOrder - 1, SortingLayer);
                restartAirborne = false;
            }
            if (ps.wallJumped && !wasWallJumped && !grounded && !ps.isDashing)
            {
                PlayBody("Walljump");
                Fx.Spawn("Walljump Puff", knight.transform.position, -Facing, Vector2.one, null, ksr.sortingOrder - 1, SortingLayer);
                restartAirborne = false;
            }
            wasDoubleJumped = ps.hasDoubleJumped;
            wasWallJumped = ps.wallJumped;

            if (!grounded) airTime += Time.deltaTime;
            if (grounded && !wasGrounded && state == State.None && airTime > 0.15f)
            {
                PlayBody(airTime > 1.1f ? "HardLand" : "Land");
                Fx.Spawn("Dust/Jump Puff", (Vector3)((Vector2)transform.position + new Vector2(0f, footLocalY)), 1f, Vector2.one, null, ksr.sortingOrder - 1, SortingLayer);
            }
            if (grounded) airTime = 0f;
            wasGrounded = grounded;

            // A one-shot is playing?
            if (overrideClip != null)
            {
                overrideTimer -= Time.deltaTime;
                bool interrupt = (ps.isSliding && state == State.None && overrideClip != "Wall Slash")
                                 || (ps.isDead && overrideClip != "Death");
                if (overrideTimer > 0f && !interrupt) return;
                overrideClip = null;
                if (queuedClip != null && !interrupt && state != State.None)
                {
                    kanim.Play(queuedClip);
                    return;
                }
                if (queuedClip != null && !interrupt && ps.isHealing)
                {
                    kanim.Play(queuedClip);
                    return;
                }
                queuedClip = null;
            }
            if (state != State.None)
            {
                if (queuedClip != null) kanim.Play(queuedClip);
                return;
            }

            string clip;
            if (ps.isDead) clip = "Death";
            else if (ps.isHealing) clip = "Focus";
            else if (ps.grappleRope != null && ps.grappleRope.isGrappling) clip = "Dash";
            else if (ps.isDashing) clip = GameManager.instance.canTeleport ? "Shadow Dash" : "Dash";
            else if (ps.isSliding) clip = "Wall Slide";
            else if (!grounded)
            {
                if (restartAirborne)
                {
                    restartAirborne = false;
                    kanim.Play("Airborne", true);
                    return;
                }
                clip = "Airborne";
            }
            else
            {
                float x = rw.GetAxisRaw("Move Horizontal");
                clip = Mathf.Abs(x) > 0.2f && Mathf.Abs(rb.velocity.x) > 0.5f && !InputHooks.Locked ? "Run" : "Idle";
            }
            kanim.Play(clip);
        }
    }

    /// <summary>The Vengeful Spirit projectile.</summary>
    public class VengefulSpirit : MonoBehaviour
    {
        private float dir;
        private LayerMask ground;
        private float life = 1.1f;
        private readonly HashSet<object> hit = new HashSet<object>();
        private int dmg;

        public void Init(float direction, LayerMask groundLayer, int sortingOrder)
        {
            dir = direction;
            ground = groundLayer;
            dmg = Combat.Scaled(Plugin.SpellDamage.Value);
            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sortingLayerID = KnightController.SortingLayer;
            sr.sortingOrder = sortingOrder;
            var a = gameObject.AddComponent<KAnim>();
            a.sr = sr;
            a.Play("Spell/Ball", true);
            transform.localScale = new Vector3(dir > 0 ? -1f : 1f, 1f, 1f);
        }

        private void Update()
        {
            float step = 24f * Time.deltaTime;
            transform.position += new Vector3(dir * step, 0f, 0f);
            Combat.HitCircle(transform.position, 0.7f, dmg, hit);
            life -= Time.deltaTime;
            if (Physics2D.OverlapCircle(transform.position, 0.2f, ground))
            {
                Fx.Spawn("Spell/Fireball Wall Impact", transform.position, transform.localScale.x, Vector2.one, null, 60, KnightController.SortingLayer);
                Destroy(gameObject);
            }
            else if (life <= 0f)
            {
                Fx.Spawn("Spell/Ball End", transform.position, transform.localScale.x, Vector2.one, null, 60, KnightController.SortingLayer);
                Destroy(gameObject);
            }
        }
    }
}
