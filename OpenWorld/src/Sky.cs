using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The sky over an Open World race, every frame: the time of day going round (the sun's and the moon's light, their colours, the sky
	/// painted by <see cref="SkyPaint"/> with the sun's and moon's discs where their light comes from, the ambient light, the fog, which
	/// takes the painted horizon's colour, the headlights) and the weather where the car is (<see cref="Storms"/>: rain or snow, the cloud that
	/// darkens everything under a storm, the rain's sound, wet roads that shine and grip less, and dry again).
	///
	/// The game sets a weather once, as a scene starts (<c>WeatherController.ApplyProfile</c>), and only moves the rain with the car after
	/// that, so all of this is done here, on the scene's own light, sky, fog and particles. The day and night looks are the two weathers
	/// <see cref="Weather"/> drew; this keeps its own copy of the day's as the active weather (so the game's rain follows the car when it is
	/// on, and the grip it reads is ours).
	///
	/// The clock is the race's (<c>TrackManager.TimeSinceRaceStart</c>, started on the host's go for every player) and the hour it starts
	/// at comes from the room's seed, so everybody in a room has the same time of day, and the same storm over the same place. Nothing here
	/// is a setting for that reason, but the debug skip.
	/// </summary>
	internal sealed class Sky : MonoBehaviour
	{
		/// <summary>Seconds of race for a whole day and night (24 hours: 100 seconds an hour).</summary>
		internal const float DaySeconds = 2400f;

		/// <summary>The light is never put lower than this (degrees): long shadows from a sun on the horizon streak and flicker.</summary>
		private const float LowestLight = 7f;

		/// <summary>The night weathers' moonlight is as bright as their maps need to race at night; a night in the open is darker.</summary>
		private const float MoonShare = 0.35f;

		/// <summary>The ambient light at night, as a share of the day's in each of red, green and blue (a blue night).</summary>
		private static readonly Color NightAmbient = new Color(0.28f, 0.31f, 0.42f);

		/// <summary>The sun's colour as it sets or rises.</summary>
		private static readonly Color Sunset = new Color(1f, 0.6f, 0.36f);

		/// <summary>One line for the debug overlay (ShowCoords): the time, the weather at the car, the nearest storm.</summary>
		internal static string Status;

		private TrackManager tm;
		private Material[] paved;
		private Material[] unpaved;
		private Storms storms;
		private float startHour;
		private float skip;
		/// <summary>The debug fast key's toggle: while on, the sky runs <see cref="FastFactor"/> times as fast, by adding to <see cref="skip"/>
		/// each frame, so turning it off leaves the sky where it got to.</summary>
		private bool fast;
		private const float FastFactor = 10f;

		private WeatherController controller;
		private WeatherProfile live;
		private WeatherProfile dayLook;
		private WeatherProfile nightLook;
		private Light sun;
		private readonly List<(SyncInteriorLight sync, float max)> cabin = new List<(SyncInteriorLight, float)>();
		/// <summary>The sky painted in code (<see cref="SkyPaint"/>), its picture and the copy of the game's panoramic sky material that shows
		/// it; null if the material could not be made, and then the day and night weathers' own skies are dimmed and swapped as before 0.18.0.</summary>
		private SkyPaint painter;
		private Texture2D paintedTexture;
		private Material paintedSky;
		private bool painted;
		private Material daySky;
		private Material nightSky;
		private float dayExposure = 1f, nightExposure = 1f;
		private Color dayTint = Color.gray;
		private float reflections = 1f;
		private AmbientProbeKeeper keeper;
		private SphericalHarmonicsL2 probe;
		private float nextProbe;
		private readonly List<(ParticleSystem ps, float rate)> rainParticles = new List<(ParticleSystem, float)>();
		private readonly List<(ParticleSystem ps, float rate)> snowParticles = new List<(ParticleSystem, float)>();
		private readonly List<(Material m, string colour, Color baseColour)> fallMaterials = new List<(Material, string, Color)>();
		private AudioSource rainSound;
		private readonly List<GameObject> headlights = new List<GameObject>();
		private int headlightsOn = -1;
		/// <summary>The falling particles' collision planes, how far round the rain the ground under them is looked at, on how many points a side.</summary>
		private readonly List<Transform> planes = new List<Transform>();
		private const double PlaneReach = 60.0;
		private const int PlaneSamples = 7;
		private readonly double[] heights = new double[PlaneSamples * PlaneSamples];
		private float nextPlanes;
		private readonly List<(Material m, Color colour, float smooth)> roads = new List<(Material, Color, float)>();

		/// <summary>With the far view on, how far one sees on the clearest day of this race (from the room's seed, under the config's
		/// FarViewDistance), the reach shown now (eased toward the weather's), and the storms round the car for the painted sky, refilled on a
		/// worker every few seconds.</summary>
		private float clearReach;
		private float reach = -1f;
		private StormField field;
		private System.Threading.Tasks.Task<StormField> filling;
		private float nextField;
		private bool fieldFailed;

		/// <summary>The storm field's samples a side and their spacing: 51,200 units across, out to where the cloud layer meets the horizon's haze.</summary>
		private const int FieldN = 64;
		private const double FieldSpacing = 800.0;

		/// <summary>The weather at the car, eased so it never jumps (a teleport, a skip).</summary>
		private float rain, cloud, wet = -1f, wetTarget, shownWet = -1f;
		private float nextWet;
		private bool ready;

		internal void Setup(TrackManager track, Material[] pavedMaterials, Material[] unpavedMaterials)
		{
			tm = track;
			paved = pavedMaterials;
			unpaved = unpavedMaterials;
			storms = new Storms(Weather.Seed);
			// Most races start in the day; a quarter start at any hour.
			ulong h = Noise.Mix((ulong)(uint)Weather.Seed ^ 0x0E7A_7140UL);
			double u = (double)(h >> 11) * (1.0 / 9007199254740992.0);
			double v = (double)(Noise.Mix(h) >> 11) * (1.0 / 9007199254740992.0);
			startHour = (float)(u < 0.75 ? 7.0 + 10.0 * v : 24.0 * v);
			// Some days are clearer than others: the clearest see the whole of the config's far view distance, the haziest two thirds of it.
			double w = (double)(Noise.Mix(h ^ 0xC1EA2UL) >> 11) * (1.0 / 9007199254740992.0);
			clearReach = Mod.FarReach.Value * (float)(0.65 + 0.35 * w);
			Mod.Log.LogInfo("Sky: the race starts at " + Clock(startHour) + ", a day lasts " + (DaySeconds / 60f).ToString("0") + " minutes; storms: a "
				+ (storms.Raininess < 0.35 ? "dry" : storms.Raininess < 0.7 ? "showery" : "wet") + " day (" + storms.Raininess.ToString("0.00") + "), wind "
				+ storms.WindSpeed.ToString("0.0") + " from " + Compass(-DetMath.Cos(storms.WindAngle), -DetMath.Sin(storms.WindAngle)) + ", snow above " + storms.SnowLine.ToString("0"));
		}

		private void Start()
		{
			try
			{
				Take();
				ready = true;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The sky could not take over the scene's weather (the race keeps the weather it started with): " + e);
			}
		}

		/// <summary>Takes over the scene's light, sky, ambient light, particles and sounds, keeping what they started as.</summary>
		private void Take()
		{
			controller = FindFirstObjectByType<WeatherController>();
			if (controller == null || controller.activeProfile == null)
			{
				throw new InvalidOperationException("the scene has no weather controller");
			}
			dayLook = controller.activeProfile;
			nightLook = Weather.Night != null ? Weather.Night : dayLook;
			live = Instantiate(dayLook);
			live.name = dayLook.name + " (live)";
			live.hideFlags = HideFlags.HideAndDontSave;
			live.rain = false;
			live.snow = false;
			live.gripExponent = 1f;
			live.gripMultiplier = 1f;
			controller.activeProfile = live;
			WeatherGrip.Current = live;

			sun = controller.directionalLight;
			foreach (SyncInteriorLight sync in FindObjectsByType<SyncInteriorLight>(FindObjectsSortMode.None))
			{
				cabin.Add((sync, sync.maxIntensity));
			}

			Paint();
			// Without a painted sky, the weathers' own: copies, since they are the game's own materials (shared with the other maps).
			if (painter == null && dayLook.skyboxMaterial != null)
			{
				daySky = new Material(dayLook.skyboxMaterial) { hideFlags = HideFlags.HideAndDontSave };
				dayExposure = daySky.HasProperty("_Exposure") ? daySky.GetFloat("_Exposure") : 1f;
				dayTint = daySky.HasProperty("_Tint") ? daySky.GetColor("_Tint") : Color.gray;
			}
			if (painter == null && nightLook.skyboxMaterial != null && nightLook != dayLook)
			{
				nightSky = new Material(nightLook.skyboxMaterial) { hideFlags = HideFlags.HideAndDontSave };
				nightExposure = nightSky.HasProperty("_Exposure") ? nightSky.GetFloat("_Exposure") : 1f;
			}
			reflections = RenderSettings.reflectionIntensity;

			// The Tokyo scene pins its ambient light (an AmbientProbeKeeper writes a stored probe back every frame), so the ambient colours
			// alone change nothing: the stored probe is scaled and handed to the keeper.
			keeper = FindFirstObjectByType<AmbientProbeKeeper>();
			probe = RenderSettings.ambientProbe;

			Fall(controller.rainEffect, rainParticles);
			Fall(controller.snowEffect, snowParticles);
			if (controller.rainEffect != null)
			{
				controller.rainEffect.SetActive(false);
			}
			if (controller.snowEffect != null)
			{
				controller.snowEffect.SetActive(false);
			}

			// The rain's sound is ours, as loud as it rains (a weather that brought its own is stopped).
			foreach (AudioSource source in controller.AmbientalAudioSources)
			{
				if (source != null && source.clip != null && Weather.IsRain(source.clip))
				{
					source.Stop();
				}
			}
			AudioClip clip = Weather.RainClip();
			if (clip != null)
			{
				rainSound = gameObject.AddComponent<AudioSource>();
				rainSound.clip = clip;
				rainSound.loop = true;
				rainSound.spatialBlend = 0f;
				rainSound.volume = 0f;
				rainSound.outputAudioMixerGroup = SoundController.AmbientGroup;
			}

			foreach (HeadLightsController lights in FindObjectsByType<HeadLightsController>(FindObjectsSortMode.None))
			{
				if (lights.spotlight != null)
				{
					headlights.Add(lights.spotlight);
				}
			}

			foreach (Material m in paved)
			{
				Road(m, true);
			}
			foreach (Material m in unpaved)
			{
				Road(m, false);
			}
			Mod.Log.LogInfo("Sky: rain and snow land on " + planes.Count + " collision plane(s) kept under them, tilted to the ground");
			Mod.Info("Sky: day " + dayLook.name + ", night " + nightLook.name + ", " + rainParticles.Count + " rain and " + snowParticles.Count + " snow particle systems, "
				+ headlights.Count + " headlights, " + cabin.Count + " cabin lights, ambient keeper " + (keeper != null ? "found" : "none") + ", rain sound " + (clip != null ? clip.name : "none"));
		}

		/// <summary>
		/// Makes the painted sky's texture and material. The game ships no procedural sky shader and none can be compiled at run time, so
		/// the material is a copy of one of the game's <c>Skybox/Panoramic</c> ones (the Forest's rainy day, Tokyo's), which keeps the
		/// shader variant the build has, set to show a whole turn of a latitude-longitude picture as it is (tint and exposure such that a
		/// texel's colour is what is seen, so the fog can match the horizon).
		/// </summary>
		private void Paint()
		{
			try
			{
				Material template = Panoramic(RenderSettings.skybox);
				RunConfig config = RunConfig.Instance;
				if (template == null && config != null)
				{
					foreach (MapDefinition map in config.maps)
					{
						template = map != null && map.weather != null ? Panoramic(map.weather.skyboxMaterial) : null;
						if (template != null)
						{
							break;
						}
					}
				}
				Shader shader = template != null ? template.shader : Shader.Find("Skybox/Panoramic");
				if (shader == null)
				{
					Mod.Log.LogWarning("Sky: no panoramic sky shader in the game, so the sky stays the weathers' own");
					return;
				}
				paintedSky = template != null ? new Material(template) : new Material(shader);
				paintedSky.name = "OpenWorld painted sky";
				paintedSky.hideFlags = HideFlags.HideAndDontSave;
				paintedSky.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
				SetIf(paintedSky, "_Mapping", 1f);
				SetIf(paintedSky, "_ImageType", 0f);
				SetIf(paintedSky, "_MirrorOnBack", 0f);
				SetIf(paintedSky, "_Layout", 0f);
				SetIf(paintedSky, "_Rotation", 0f);
				SetIf(paintedSky, "_Exposure", 1f);
				// The shader multiplies by the tint and unity_ColorSpaceDouble (2 in gamma, 4.59 in linear): a tint that undoes that.
				float tint = QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.LinearToGammaSpace(1f / 4.594794f) : 0.5f;
				if (paintedSky.HasProperty("_Tint"))
				{
					paintedSky.SetColor("_Tint", new Color(tint, tint, tint, 1f));
				}
				paintedTexture = new Texture2D(SkyPaint.W, SkyPaint.H, TextureFormat.RGBA32, false, false)
				{
					name = "OpenWorld painted sky",
					filterMode = FilterMode.Point,
					wrapModeU = TextureWrapMode.Repeat,
					wrapModeV = TextureWrapMode.Clamp,
					anisoLevel = 0,
					hideFlags = HideFlags.HideAndDontSave,
				};
				paintedSky.SetTexture("_MainTex", paintedTexture);
				painter = new SkyPaint(Weather.Seed);
				Mod.Info("Sky: painted sky on a copy of " + (template != null ? template.name : "Skybox/Panoramic") + " (" + shader.name + "), keywords " + string.Join(" ", paintedSky.shaderKeywords));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Sky: could not make the painted sky, so the sky stays the weathers' own: " + e.Message);
				painter = null;
				if (paintedSky != null)
				{
					Destroy(paintedSky);
					paintedSky = null;
				}
				if (paintedTexture != null)
				{
					Destroy(paintedTexture);
					paintedTexture = null;
				}
			}
		}

		private static Material Panoramic(Material m)
		{
			return m != null && m.shader != null && m.shader.name == "Skybox/Panoramic" ? m : null;
		}

		private static void SetIf(Material m, string property, float value)
		{
			if (m.HasProperty(property))
			{
				m.SetFloat(property, value);
			}
		}

		/// <summary>Notes a falling effect's particle systems and their rates, and gives its materials copies that can be dimmed at night.</summary>
		private void Fall(GameObject effect, List<(ParticleSystem, float)> into)
		{
			if (effect == null)
			{
				return;
			}
			foreach (ParticleSystem ps in effect.GetComponentsInChildren<ParticleSystem>(true))
			{
				into.Add((ps, ps.emission.rateOverTimeMultiplier));
				Land(ps);
				ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
				if (r == null || r.sharedMaterial == null)
				{
					continue;
				}
				Material m = new Material(r.sharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
				r.sharedMaterial = m;
				string colour = m.HasProperty("_BaseColor") ? "_BaseColor" : m.HasProperty("_Color") ? "_Color" : m.HasProperty("_TintColor") ? "_TintColor" : null;
				if (colour != null)
				{
					fallMaterials.Add((m, colour, m.GetColor(colour)));
				}
			}
		}

		/// <summary>
		/// Makes a falling particle system that collides land on the world's own ground. The game's rain and snow collide with a plane
		/// (<c>Weather/ParticleColliderPlane</c>, at Tokyo's ground level; a collision plane, so switching its collider off changes nothing):
		/// in the Open World that was the spawn's height, so the drops stopped and splashed in mid-air over low ground. Collision with the
		/// world's colliders was tried (0.17.0 before its test) and failed: at a quality the drops can afford Unity caches the colliders as
		/// static, and the floating origin moves them all and the streamer swaps them, so after the first shift the drops hit ground that
		/// was no longer there and vanished. So the plane stays, and <see cref="MovePlanes"/> keeps it under the rain, tilted to the
		/// ground there: no drop stops in the air. The drop dies where it hits (its
		/// splash, a collision sub-emitter, comes up there) and lives twice as long, to reach a valley floor below the emitter.
		/// </summary>
		private void Land(ParticleSystem ps)
		{
			try
			{
				ParticleSystem.CollisionModule collision = ps.collision;
				ParticleSystem.SubEmittersModule subs = ps.subEmitters;
				Mod.Info("Sky: particles " + ps.name + ": collision " + (collision.enabled ? collision.type + " (" + collision.planeCount + " planes, quality " + collision.quality + ", lifetime loss " + collision.lifetimeLossMultiplier + ")" : "off")
					+ ", " + subs.subEmittersCount + " sub-emitters" + (subs.subEmittersCount > 0 ? " (" + subs.GetSubEmitterType(0) + " " + (subs.GetSubEmitterSystem(0) != null ? subs.GetSubEmitterSystem(0).name : "none") + ")" : "")
					+ ", life " + ps.main.startLifetimeMultiplier.ToString("0.0") + " s");
				if (!collision.enabled || collision.type != ParticleSystemCollisionType.Planes)
				{
					return;
				}
				for (int i = 0; i < collision.planeCount; i++)
				{
					Transform plane = collision.GetPlane(i);
					if (plane != null && !planes.Contains(plane))
					{
						planes.Add(plane);
					}
				}
				collision.lifetimeLossMultiplier = 1f;
				collision.bounceMultiplier = 0f;
				ParticleSystem.MainModule main = ps.main;
				main.startLifetimeMultiplier *= 2f;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not make the particles " + ps.name + " land on the ground: " + e.Message);
			}
		}

		/// <summary>
		/// Puts the falling particles' collision planes under the rain, ten times a second, tilted to the ground there: a plane fitted
		/// to the ground or water on a 7 by 7 grid round the rain (least squares), then lowered until it is nowhere above that ground.
		/// On a hill, going up or down it, the plane lies along the slope, so the drops land and splash on the road ahead and behind; a
		/// level plane at the lowest ground would sit far under the road there. Over a valley or a ridge the fit is lowered by the bump
		/// and some drops go into the ground, out of sight; none ever stops in the air. Each sample is about 3 µs. The planes are in
		/// scene coordinates like everything else, so this also puts them right after an origin shift.
		/// </summary>
		private void MovePlanes()
		{
			GameObject effect = controller.rainEffect != null && controller.rainEffect.activeSelf ? controller.rainEffect : controller.snowEffect;
			if (planes.Count == 0 || effect == null || !effect.activeSelf || Time.time < nextPlanes)
			{
				return;
			}
			nextPlanes = Time.time + 0.1f;
			Vector3 at = effect.transform.position;
			Origin.World(at, out double x, out double z);
			const int n = PlaneSamples / 2;
			double step = PlaneReach / n;
			for (int j = -n; j <= n; j++)
			{
				for (int i = -n; i <= n; i++)
				{
					double px = x + i * step, pz = z + j * step;
					double ground = WorldGen.Height(px, pz);
					if (Rivers.Water(px, pz, out double level))
					{
						ground = Math.Max(ground, level + Network.Lift);
					}
					heights[(j + n) * PlaneSamples + i + n] = ground;
				}
			}
			// On a square grid centred here the least-squares plane y = a + b dx + c dz has a the mean and b, c the plain slopes.
			double sum = 0.0, sx = 0.0, sz = 0.0, xx = 0.0;
			for (int j = -n; j <= n; j++)
			{
				for (int i = -n; i <= n; i++)
				{
					double h = heights[(j + n) * PlaneSamples + i + n];
					sum += h;
					sx += h * i * step;
					sz += h * j * step;
					xx += i * step * i * step;
				}
			}
			double a = sum / (PlaneSamples * PlaneSamples), b = sx / xx, c = sz / xx;
			double above = 0.0;
			for (int j = -n; j <= n; j++)
			{
				for (int i = -n; i <= n; i++)
				{
					above = Math.Max(above, a + b * i * step + c * j * step - heights[(j + n) * PlaneSamples + i + n]);
				}
			}
			Vector3 position = new Vector3(at.x, (float)(a - above) - 0.05f, at.z);
			Quaternion tilt = Quaternion.FromToRotation(Vector3.up, new Vector3((float)-b, 1f, (float)-c).normalized);
			foreach (Transform plane in planes)
			{
				if (plane != null)
				{
					plane.SetPositionAndRotation(position, tilt);
				}
			}
		}

		private void Road(Material m, bool shines)
		{
			if (m == null || !m.HasProperty("_BaseColor"))
			{
				return;
			}
			roads.Add((m, m.GetColor("_BaseColor"), shines && m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1f));
		}

		private void Update()
		{
			if (!ready || tm == null || tm.playerCarController == null)
			{
				return;
			}
			if (Mod.WeatherSkip.Value && Mod.WeatherSkipKey != null && Mod.WeatherSkipKey.Down)
			{
				bool back = GameInput.GetKey(KeyCode.LeftShift) || GameInput.GetKey(KeyCode.RightShift);
				skip += back ? -300f : 300f;
				TextController.EnqueueRandomText(1f, (back ? "Weather back" : "Weather on") + " 5 min");
			}
			if (Mod.WeatherSkip.Value && Mod.WeatherFastKey != null && Mod.WeatherFastKey.Down)
			{
				fast = !fast;
				TextController.EnqueueRandomText(1f, fast ? "Weather x" + FastFactor.ToString("0") : "Weather x1");
			}
			if (fast && Mod.WeatherSkip.Value)
			{
				skip += Time.deltaTime * (FastFactor - 1f);
			}
			try
			{
				Step();
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The sky stopped: " + e);
				ready = false;
			}
		}

		private void Step()
		{
			float dt = Time.deltaTime;
			double t = tm.TimeSinceRaceStart + (double)skip;
			float hour = Mathf.Repeat(startHour + (float)(t / DaySeconds * 24.0), 24f);

			// ---- the weather at the car ----
			Transform car = tm.playerCarController.transform;
			Origin.World(car.position, out double x, out double z);
			double height = car.position.y - Network.Lift;
			storms.At(x, z, t, height, out double rainHere, out double cloudHere);
			float snowShare = (float)storms.SnowShare(height);
			if (wet < 0f || Time.time >= nextWet)
			{
				nextWet = Time.time + 1f;
				wetTarget = (float)storms.Wetness(x, z, t, height);
				if (wet < 0f)
				{
					wet = wetTarget;
					rain = (float)rainHere;
					cloud = (float)cloudHere;
				}
			}
			rain = Mathf.MoveTowards(rain, (float)rainHere, dt * 0.08f);
			cloud = Mathf.MoveTowards(cloud, (float)cloudHere, dt * 0.05f);
			wet = Mathf.MoveTowards(wet, wetTarget, dt * 0.03f);

			// ---- the sun and the moon ----
			// The sun rises in the east at 6, is south at noon and sets in the west at 18; the moon goes round opposite it.
			SkyPaint.State state = new SkyPaint.State();
			SkyPaint.Place(ref state, hour, out float turn);
			float elevation = state.SunElevation;
			float daylight = Step(-6f, 10f, elevation);
			float sunUp = Step(-2f, 6f, elevation);
			float moonUp = Step(-2f, -8f, elevation);
			float sunset = sunUp * (1f - Step(2f, 20f, elevation));
			bool moon = elevation < -2f;
			float shownElevation = Mathf.Max(LowestLight, Mathf.Abs(elevation) * (moon ? 0.8f : 1f));
			float azimuth = moon ? -turn + 180f : -turn;
			Vector3 toLight = Quaternion.Euler(0f, -azimuth, 0f) * (Quaternion.Euler(0f, 0f, shownElevation) * Vector3.right);
			sun.transform.rotation = Quaternion.LookRotation(-toLight, Vector3.up);

			float dim = 1f - 0.55f * cloud;
			Color sunColour = dayLook.directionalLightColor * Color.Lerp(Color.white, Sunset, sunset);
			sun.color = moon ? nightLook.directionalLightColor : sunColour;
			sun.intensity = (sunUp * dayLook.directionalLightIntensity + moonUp * nightLook.directionalLightIntensity * MoonShare) * dim;
			float level = (sunUp + moonUp * MoonShare * 0.6f) * dim;
			foreach ((SyncInteriorLight sync, float max) in cabin)
			{
				if (sync != null)
				{
					sync.maxIntensity = max * level;
					Light l = sync.GetComponent<Light>();
					if (l != null)
					{
						l.color = sun.color;
					}
				}
			}

			// ---- the ambient light ----
			Color ambient = Color.Lerp(NightAmbient, Color.white, daylight) * Color.Lerp(Color.white, new Color(1f, 0.82f, 0.68f), sunset * 0.6f) * (1f - 0.4f * cloud);
			if (Time.time >= nextProbe)
			{
				nextProbe = Time.time + 0.2f;
				SphericalHarmonicsL2 scaled = probe;
				for (int i = 0; i < 9; i++)
				{
					scaled[0, i] *= ambient.r;
					scaled[1, i] *= ambient.g;
					scaled[2, i] *= ambient.b;
				}
				if (keeper != null)
				{
					keeper.Store(scaled);
				}
				RenderSettings.ambientProbe = scaled;
				RenderSettings.reflectionIntensity = reflections * Mathf.Lerp(0.15f, 1f, daylight) * (1f - 0.4f * cloud);
			}

			// ---- the sky ----
			Color? horizon = null;
			if (painter != null)
			{
				try
				{
					horizon = PaintSky(ref state, x, z, t);
				}
				catch (Exception e)
				{
					// The last picture stays up and the fog goes back to the weathers' colours; the rest of the sky carries on.
					Mod.Log.LogWarning("Sky: the painted sky stopped: " + e);
					painter = null;
				}
			}
			bool night = daylight < 0.2f && nightSky != null;
			Material sky = night ? nightSky : daySky;
			if (painter == null && sky != null && RenderSettings.skybox != sky)
			{
				RenderSettings.skybox = sky;
			}
			if (daySky != null && daySky.HasProperty("_Exposure"))
			{
				daySky.SetFloat("_Exposure", dayExposure * Mathf.Max(0.06f, daylight * daylight) * (1f - 0.5f * cloud));
			}
			if (daySky != null && daySky.HasProperty("_Tint"))
			{
				daySky.SetColor("_Tint", dayTint * Color.Lerp(Color.white, Sunset, sunset * 0.7f));
			}
			if (nightSky != null)
			{
				if (nightSky.HasProperty("_Exposure"))
				{
					nightSky.SetFloat("_Exposure", nightExposure * Mathf.Lerp(1f, 0.3f, daylight * 5f) * (1f - 0.5f * cloud));
				}
				if (nightSky.HasProperty("_Rotation"))
				{
					// The stars turn through the night.
					nightSky.SetFloat("_Rotation", hour * 15f);
				}
			}

			// ---- the fog ----
			// With the painted sky, the horizon's colour, so the world fades into the sky; else the weathers' fog colours, blended.
			Color fog;
			if (horizon.HasValue)
			{
				fog = horizon.Value;
			}
			else
			{
				fog = Color.Lerp(nightLook.fogColor, dayLook.fogColor, daylight) * Color.Lerp(Color.white, new Color(1f, 0.75f, 0.6f), sunset * 0.5f);
				float grey = fog.grayscale;
				fog = Color.Lerp(fog, new Color(grey, grey, grey), cloud * 0.6f) * (1f - 0.3f * cloud);
			}
			float thin = Mathf.Lerp(1f, 0.45f, rain);
			float fogEnd = Mathf.Lerp(Mathf.Min(nightLook.fogEnd, dayLook.fogEnd), dayLook.fogEnd, daylight) * thin;
			float fogStart = Mathf.Lerp(Mathf.Min(nightLook.fogStart, dayLook.fogStart), dayLook.fogStart, daylight) * thin;
			// With the far view, how far one sees is ours, from the weather at the car (the weathers' fogs were made for arenas, some
			// ending at 300): the day's clear reach, a little less at night, brought in under cloud and closer still in the rain. Eased, so
			// it never jumps (the far view coming on, a teleport).
			float target = fogEnd;
			if (FarView.Showing)
			{
				target = clearReach * Mathf.Lerp(0.75f, 1f, daylight) * (1f - 0.5f * cloud) * Mathf.Lerp(1f, 0.08f, rain);
				target = Mathf.Max(target, fogEnd);
			}
			reach = reach < 0f ? target : Mathf.MoveTowards(reach, target, dt * Mathf.Max(600f, reach * 0.4f));
			if (FarView.Showing || reach > fogEnd + 1f)
			{
				fogEnd = reach;
				fogStart = reach * 0.03f;
			}
			RenderSettings.fog = true;
			RenderSettings.fogColor = fog;
			RenderSettings.fogEndDistance = fogEnd;
			RenderSettings.fogStartDistance = fogStart;

			// ---- rain and snow ----
			float falling = rain < 0.02f ? 0f : rain;
			Fall(controller.rainEffect, rainParticles, falling * (1f - snowShare), out live.rain);
			Fall(controller.snowEffect, snowParticles, falling * snowShare, out live.snow);
			MovePlanes();
			float bright = Mathf.Lerp(0.3f, 1f, daylight) * (1f - 0.3f * cloud);
			foreach ((Material m, string colour, Color baseColour) in fallMaterials)
			{
				m.SetColor(colour, new Color(baseColour.r * bright, baseColour.g * bright, baseColour.b * bright, baseColour.a));
			}
			if (rainSound != null)
			{
				float loud = 0.3f * falling * (1f - snowShare);
				rainSound.volume = loud;
				if (loud > 0f && !rainSound.isPlaying)
				{
					rainSound.Play();
				}
				else if (loud <= 0f && rainSound.isPlaying)
				{
					rainSound.Stop();
				}
			}

			// ---- the headlights: on from dusk to dawn, and under a black sky ----
			int on = daylight < 0.4f || cloud > 0.8f ? 1 : 0;
			if (on != headlightsOn)
			{
				headlightsOn = on;
				foreach (GameObject spot in headlights)
				{
					if (spot != null)
					{
						spot.SetActive(on == 1);
					}
				}
			}

			// ---- wet roads: darker, shinier and less grip; snow lying grips less still ----
			if (Mathf.Abs(wet - shownWet) > 0.01f)
			{
				shownWet = wet;
				foreach ((Material m, Color colour, float smooth) in roads)
				{
					float d = smooth >= 0f ? 0.28f : 0.18f;
					m.SetColor("_BaseColor", colour * (1f - d * wet));
					if (smooth >= 0f)
					{
						m.SetFloat("_Smoothness", Mathf.Lerp(smooth, 0.6f, wet));
					}
				}
			}
			live.gripExponent = 1f + wet * (0.06f + 0.05f * snowShare);
			live.gripMultiplier = 1f - wet * (0.04f + 0.02f * snowShare);

			if (Mod.ShowCoords.Value)
			{
				string near = "no storm";
				if (storms.Nearest(x, z, t, out double dx, out double dz, out double edge, out double strength))
				{
					near = edge <= 0.0 ? "in a storm (" + strength.ToString("0.00") + ")" : "storm " + (edge / 1000.0).ToString("0.0") + " km " + Compass(dx, dz) + " (" + strength.ToString("0.00") + ")";
				}
				Status = Clock(hour) + (moon ? " moon" : " sun") + " " + elevation.ToString("0") + "°   " + (snowShare > 0.5f ? "snow " : "rain ") + rain.ToString("0.00") + "  cloud " + cloud.ToString("0.00")
					+ "  wet " + wet.ToString("0.00") + "  grip x" + live.gripMultiplier.ToString("0.00") + "^" + live.gripExponent.ToString("0.00") + "   " + near + (skip != 0f ? "   skipped " + (skip / 60f).ToString("0") + " min" : "");
			}
		}

		/// <summary>
		/// Hands the painted sky this frame's state (the sun, moon and stars already placed in <paramref name="state"/>; the cloud at the
		/// car; the race's haze; where the car is under the drifting cloud), uploads a picture when one is finished, and gives back the
		/// horizon's colour for the fog.
		/// </summary>
		private Color PaintSky(ref SkyPaint.State state, double x, double z, double t)
		{
			state.Cloud = cloud;
			// The share of the sky under cloud: some on a dry day, more on a wet one, all of it under a storm.
			state.Coverage = Mathf.Clamp01(0.06f + 0.3f * (float)storms.Raininess + 0.65f * cloud);
			state.DayHaze = Rgb(dayLook.fogColor);
			state.NightHaze = Rgb(nightLook.fogColor);
			// The cloud pattern stays put over the land and drifts downwind: the car's position less the wind's way since the start.
			const double tile = 24000.0;
			double wx = x - storms.WindSpeed * DetMath.Cos(storms.WindAngle) * t;
			double wz = z - storms.WindSpeed * DetMath.Sin(storms.WindAngle) * t;
			state.CloudX = (float)(wx - Math.Floor(wx / tile) * tile);
			state.CloudZ = (float)(wz - Math.Floor(wz / tile) * tile);
			Field(x, z, t);
			state.Storm = field;
			state.CarX = (float)x;
			state.CarZ = (float)z;
			state.Reach = reach > 0f ? reach : RenderSettings.fogEndDistance;
			bool done;
			if (!painted)
			{
				painter.All(state);
				painted = true;
				done = true;
			}
			else
			{
				done = painter.Frame(state);
			}
			if (done)
			{
				paintedTexture.SetPixelData(painter.Pixels, 0);
				paintedTexture.Apply(false, false);
			}
			if (RenderSettings.skybox != paintedSky)
			{
				RenderSettings.skybox = paintedSky;
			}
			SkyPaint.Palette(state, out _, out SkyPaint.Rgb horizon, out _);
			return new Color(horizon.R, horizon.G, horizon.B, 1f);
		}

		/// <summary>
		/// Keeps <see cref="field"/>, the storms round the car for the painted sky, up to date: refilled on a worker every 3 seconds round
		/// where the car is (the storms drift at most 33 units in that time, under a texel at a kilometre), the first at once so the first
		/// picture has them.
		/// </summary>
		private void Field(double x, double z, double t)
		{
			if (fieldFailed)
			{
				return;
			}
			if (filling != null && filling.IsCompleted)
			{
				if (filling.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
				{
					field = filling.Result;
				}
				else
				{
					fieldFailed = true;
					Mod.Log.LogWarning("Sky: the storms seen from afar stopped: " + filling.Exception);
				}
				filling = null;
			}
			if (filling != null || Time.time < nextField)
			{
				return;
			}
			nextField = Time.time + 3f;
			Storms st = storms;
			if (field == null)
			{
				StormField first = new StormField(FieldN, FieldSpacing, x, z);
				st.Fill(first, t);
				field = first;
				return;
			}
			filling = System.Threading.Tasks.Task.Run(() =>
			{
				StormField next = new StormField(FieldN, FieldSpacing, x, z);
				st.Fill(next, t);
				return next;
			});
		}

		private static SkyPaint.Rgb Rgb(Color c)
		{
			return new SkyPaint.Rgb(c.r, c.g, c.b);
		}

		/// <summary>Sets a falling effect going as hard as <paramref name="amount"/> (0 stops it), within the game's graphics budget.</summary>
		private static void Fall(GameObject effect, List<(ParticleSystem ps, float rate)> systems, float amount, out bool on)
		{
			on = amount > 0f && effect != null;
			if (effect == null)
			{
				return;
			}
			if (effect.activeSelf != on)
			{
				effect.SetActive(on);
			}
			if (!on)
			{
				return;
			}
			foreach ((ParticleSystem ps, float rate) in systems)
			{
				if (ps != null)
				{
					// Setting the rate takes these systems out of the game's render budget, so its weather cut is applied here.
					ParticleSystem.EmissionModule emission = ps.emission;
					emission.rateOverTimeMultiplier = rate * amount * WeatherBudget.Rate(ps);
				}
			}
		}

		private void OnDestroy()
		{
			Status = null;
			if (live != null)
			{
				if (WeatherGrip.Current == live)
				{
					WeatherGrip.Current = null;
				}
				Destroy(live);
			}
			if (paintedSky != null)
			{
				Destroy(paintedSky);
			}
			if (paintedTexture != null)
			{
				Destroy(paintedTexture);
			}
			if (daySky != null)
			{
				Destroy(daySky);
			}
			if (nightSky != null)
			{
				Destroy(nightSky);
			}
			foreach ((Material m, string _, Color _) in fallMaterials)
			{
				if (m != null)
				{
					Destroy(m);
				}
			}
		}

		/// <summary>0 at or below <paramref name="a"/>, 1 at or beyond <paramref name="b"/> (which may be below a), smooth in between.</summary>
		private static float Step(float a, float b, float x)
		{
			float u = Mathf.Clamp01((x - a) / (b - a));
			return u * u * (3f - 2f * u);
		}

		private static string Clock(float hour)
		{
			int minutes = (int)(Mathf.Repeat(hour, 24f) * 60f);
			return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
		}

		/// <summary>The compass point of the direction (dx east, dz north).</summary>
		private static string Compass(double dx, double dz)
		{
			string[] points = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
			double angle = Math.Atan2(dz, dx) / (2.0 * Math.PI) * 8.0;
			return points[(int)Math.Floor(angle + 8.5) % 8];
		}
	}
}
