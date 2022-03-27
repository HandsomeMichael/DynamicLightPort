using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.Graphics.Effects;
using Terraria.Graphics;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.ModLoader.Config.UI;

namespace DynamicLightPort
{
	public class DynamicLightPort : Mod
	{

		public static bool use_lighting = false;
		public static int shadow_res_power = 9; // 6 - 12, rec 9
		public static float cutoff = 1.0f; // 0 - Inf (10-ish?)
		public static int maxlights = 0;
		public static float shadow_smoothness = 5.0f; // 0 - Inf (10-ish?)
		public static float brightness_dist = 1.0f; // 0 - Inf (5-ish?)
		public static float dark_brightness = 0.75f; // 0 - 1
		public static float bright_brightness = 3.0f; // 1 - Inf (10-ish?)
		public static float brightness_growth_base = 1.2f; // 1 - Inf (5-ish?)
		public static float brightness_growth_rate = 1.5f; // 0 - Inf (10-ish?)
		public static bool increase_surface = true;
		public static bool show_sun = false;

		public static bool rebuild = false;

		//The Alpha channel of the shadowmap corresponds to what can cast shadows
		public RenderTarget2D shadowmap;
		public RenderTarget2D shadowcaster;
		public RenderTarget2D mappedshadows;
		public RenderTarget2D[] shadowreducer;
		public RenderTarget2D lightmap;
		public RenderTarget2D lightmapswap;
		public RenderTarget2D screen;

		bool clearNextFrame = false;

		public struct Light
        {
			public int x;
			public int y;
			public Vector3 color;
			public bool isSun;
			public Light(int x, int y, Vector3 color, bool isSun = false)
            {
				this.x = x;
				this.y = y;
				this.color = color;
				this.isSun = isSun;
            }
        }
		public List<Light> lights = new List<Light>();

		public static Effect FancyLights;

		public override void Load()
		{
			if (!Main.dedServ)
			{
				FancyLights = GetEffect("Effects/Lights");
			}
			On.Terraria.Graphics.Effects.FilterManager.EndCapture += FilterManager_EndCapture;
			On.Terraria.Lighting.AddLight_int_int_float_float_float += AddLightPatch;
			On.Terraria.Lighting.LightTiles += LightTilesPatch;
			On.Terraria.Main.LoadWorlds += Main_LoadWorlds;
			Main.OnResolutionChanged += Main_OnResolutionChanged;
		}

		public override void PostSetupContent()
		{
			if (!Main.dedServ)
			{
				FancyLights = GetEffect("Effects/Lights");
			}
		}

		public override void Unload()
		{
			On.Terraria.Graphics.Effects.FilterManager.EndCapture -= FilterManager_EndCapture;
			On.Terraria.Main.LoadWorlds -= Main_LoadWorlds;
		}

		private void Main_LoadWorlds(On.Terraria.Main.orig_LoadWorlds orig)
		{
			orig();
			if (screen == null)
			{
				GraphicsDevice gd = Main.instance.GraphicsDevice;
				screen = new RenderTarget2D(gd, gd.PresentationParameters.BackBufferWidth , gd.PresentationParameters.BackBufferHeight , false, gd.PresentationParameters.BackBufferFormat, (DepthFormat)0);
				lightmap = new RenderTarget2D(gd, gd.PresentationParameters.BackBufferWidth, gd.PresentationParameters.BackBufferHeight, false, gd.PresentationParameters.BackBufferFormat, (DepthFormat)0);
				lightmapswap = new RenderTarget2D(gd, gd.PresentationParameters.BackBufferWidth, gd.PresentationParameters.BackBufferHeight, false, gd.PresentationParameters.BackBufferFormat, (DepthFormat)0);
				shadowmap = new RenderTarget2D(gd, gd.PresentationParameters.BackBufferWidth + Main.offScreenRange * 2, gd.PresentationParameters.BackBufferHeight + Main.offScreenRange * 2, false, gd.PresentationParameters.BackBufferFormat, (DepthFormat)0);
				shadowcaster = new RenderTarget2D(gd, 1 << shadow_res_power, 1 << shadow_res_power, false, gd.PresentationParameters.BackBufferFormat, (DepthFormat)0);
				mappedshadows = new RenderTarget2D(gd, 1 << shadow_res_power, 1 << shadow_res_power, false, gd.PresentationParameters.BackBufferFormat, (DepthFormat)0);
				shadowreducer = new RenderTarget2D[shadow_res_power - 1];
				for(int i = 1; i < shadow_res_power; i++)
                {
					shadowreducer[i - 1] = new RenderTarget2D(gd, 1 << i, 1 << shadow_res_power, false, gd.PresentationParameters.BackBufferFormat, (DepthFormat)0);
				}
			}
		}

		private void Main_OnResolutionChanged(Vector2 obj)
		{
			screen = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth , Main.screenHeight );
			lightmap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
			lightmapswap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
			shadowmap = new RenderTarget2D(Main.instance.GraphicsDevice , Main.screenWidth + Main.offScreenRange * 2, Main.screenHeight + Main.offScreenRange * 2);
			shadowcaster = new RenderTarget2D(Main.instance.GraphicsDevice, 1 << shadow_res_power, 1 << shadow_res_power);
			mappedshadows = new RenderTarget2D(Main.instance.GraphicsDevice, 1 << shadow_res_power, 1 << shadow_res_power);
			shadowreducer = new RenderTarget2D[shadow_res_power - 1];
			for (int i = 1; i < shadow_res_power; i++)
			{
				shadowreducer[i - 1] = new RenderTarget2D(Main.instance.GraphicsDevice, 1 << i, 1 << shadow_res_power);
			}
		}
		//AddLight(int i, int j, float R, float G, float B)
		private void AddLightPatch(On.Terraria.Lighting.orig_AddLight_int_int_float_float_float orig, 
		int i, int j, float R, float G , float B)
		{
			orig(i, j,R,G,B);
			if (use_lighting)
			{
				lights.Add(new Light(i * 16 + 8, j * 16 + 8, new Vector3(R,G,B)));
			}
		}
		// LightTiles(int firstX, int lastX, int firstY, int lastY)
		private void LightTilesPatch(On.Terraria.Lighting.orig_LightTiles orig ,int firstX, int lastX, int firstY, int lastY)
		{
			orig(firstX,lastX,firstY,lastY);
			if (use_lighting)
			{
				clearNextFrame = true;
			}
		}

		private void FilterManager_EndCapture(On.Terraria.Graphics.Effects.FilterManager.orig_EndCapture orig, FilterManager self)
		{
			GraphicsDevice graphicsDevice = Main.instance.GraphicsDevice;

            if (rebuild)
            {
				screen = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
				lightmap = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
				lightmapswap = new RenderTarget2D(graphicsDevice, Main.screenWidth, Main.screenHeight);
				shadowmap = new RenderTarget2D(graphicsDevice, Main.screenWidth + Main.offScreenRange * 2, Main.screenHeight + Main.offScreenRange * 2);
				shadowcaster = new RenderTarget2D(graphicsDevice, 1 << shadow_res_power, 1 << shadow_res_power);
				mappedshadows = new RenderTarget2D(graphicsDevice, 1 << shadow_res_power, 1 << shadow_res_power);
				shadowreducer = new RenderTarget2D[shadow_res_power - 1];
				for (int i = 1; i < shadow_res_power; i++)
				{
					shadowreducer[i - 1] = new RenderTarget2D(graphicsDevice, 1 << i, 1 << shadow_res_power);
				}
				rebuild = false;
			}

			//LightingEngine lightingEngine = typeof(Lighting).GetField("_activeEngine", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) as LightingEngine;

			if (use_lighting /*&& lightingEngine != null*/)
            {
				/*TileLightScanner tileScanner = typeof(LightingEngine).GetField("_tileScanner", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(lightingEngine) as TileLightScanner;*/
				SpriteBatch spriteBatch = Main.spriteBatch;

				//Save Swap
				graphicsDevice.SetRenderTarget(Main.screenTargetSwap);
				graphicsDevice.Clear(Color.Transparent);
				spriteBatch.Begin((SpriteSortMode)0, BlendState.AlphaBlend);
				spriteBatch.Draw(Main.screenTarget, Vector2.Zero, Color.White);
				spriteBatch.End();

				//Draw Tile Shadowmap
				graphicsDevice.SetRenderTarget(shadowmap);
				graphicsDevice.Clear(Color.Transparent);
				spriteBatch.Begin();
				//protected void DrawTiles
				object[] args = new object[] { true, -1};
				typeof(Main).GetMethod("DrawTiles", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(Main.instance,args);
				spriteBatch.End();

				//Do PerLightShading
				graphicsDevice.SetRenderTarget(lightmapswap);
				graphicsDevice.Clear(new Color(0, 0, 0, 0));
				graphicsDevice.SetRenderTarget(lightmap);
				graphicsDevice.Clear(new Color(0, 0, 0, 0));
				BlendState blendState = new BlendState();
				blendState.ColorBlendFunction = BlendFunction.Max;
				blendState.AlphaBlendFunction = BlendFunction.Max;
				blendState.ColorSourceBlend = Blend.One;
				blendState.AlphaSourceBlend = Blend.One;
				blendState.ColorDestinationBlend = Blend.One;
				blendState.AlphaDestinationBlend = Blend.One;
				blendState.ColorWriteChannels = ColorWriteChannels.All;

				//Vector2 unscaledPosition = Main.Camera.UnscaledPosition;
				//Vector2 vector = new Vector2((float)Main.offScreenRange, (float)Main.offScreenRange);

				//GetScreenDrawArea(Vector2 screenPosition, Vector2 offSet, 
				// out int firstTileX, out int lastTileX, out int firstTileY, out int lastTileY)

				//object[] args = new object[] { unscaledPosition,
				// vector + (Main.Camera.UnscaledPosition - Main.Camera.ScaledPosition), null, null, null, null };

				//typeof(TileDrawing).GetMethod("GetScreenDrawArea", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tilesRenderer, args);

				int firstTileX = Convert.ToInt32(typeof(Lighting).GetField("firstTileX", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
				int lastTileX = Convert.ToInt32(typeof(Lighting).GetField("lastTileX", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
				int firstTileY = Convert.ToInt32(typeof(Lighting).GetField("firstTileY", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));
				int lastTileY = Convert.ToInt32(typeof(Lighting).GetField("lastTileY", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null));

				/*
				private static int firstTileX;
				private static int lastTileX;
				private static int firstTileY;
				private static int lastTileY;
				*/
				int cnt = 0;

				List<Light> lightsSort = new List<Light>();

				for (int i = (int) firstTileY; i < (int) lastTileY + 4; i++)
				{
					for (int j = (int) firstTileX - 2; j < (int) lastTileX + 2; j++)
					{
						Tile tile = Main.tile[j, i];
						if (tile == null)
							continue;

						if (!Main.tileLighted[tile.type])
							continue;

						Vector3 color;
						TileLightScanner.GetTileLight(j, i, out color);

						lightsSort.Add(new Light(j * 16 + 8, i * 16 + 8, color));
					}
				}

				int xmin = ((int)firstTileX - 2) * 16 + 8;
				int xmax = ((int)lastTileX + 2) * 16 + 8;
				int ymin = ((int)firstTileY) * 16 + 8;
				int ymax = ((int)lastTileY + 4) * 16 + 8;
				foreach (Light light in lights){
					if (light.x >= xmin && light.x <= xmax && light.y >= ymin && light.y <= ymax)
						lightsSort.Add(light);
                }

                if (show_sun)
                {
					Vector2 sunPos = GetSunPos();
					float sunFactor = 0.1f;
					Vector3 sunColor = new Vector3(Main.bgColor.R * sunFactor, Main.bgColor.G * sunFactor, Main.bgColor.B * sunFactor);
					lightsSort.Add(new Light((int) Main.screenPosition.X + (int)sunPos.X, (int)sunPos.Y, sunColor, true));
                }

				lightsSort.Sort((l, r) => GetBrightness(r).CompareTo(GetBrightness(l)));

				int targetCnt = (maxlights == 0 || lightsSort.Count < maxlights) ? lightsSort.Count : maxlights;
				for (int i = 0; i < targetCnt; i++)
                {
					bool rendered = PerLightShading(ref graphicsDevice, ref spriteBatch, blendState, lightsSort[i]);
					if (!rendered)
						cnt++;
				}

				//Main.NewText($"Cut: {cnt}", 255, 240, 20);

				if (clearNextFrame)
                {
					clearNextFrame = false;
					lights.Clear();
                }

				//Render Swap and Screen to Final
				graphicsDevice.SetRenderTarget(Main.screenTarget);
				graphicsDevice.Clear(Color.Transparent);
				spriteBatch.Begin((SpriteSortMode)1, BlendState.Opaque);

				FancyLights.Parameters["darkBrightness"].SetValue(increase_surface ? (dark_brightness + (1 - dark_brightness) * 0.8f / (1f + (float)Math.Exp(0.01 * (double)(Main.screenPosition.Y + Main.screenHeight / 2 - Main.worldSurface * 16.0f)))) : dark_brightness);
				FancyLights.Parameters["brightBrightness"].SetValue(bright_brightness);
				FancyLights.Parameters["brightnessGrowthBase"].SetValue(brightness_growth_base);
				FancyLights.Parameters["brightnessGrowthRate"].SetValue(brightness_growth_rate);
				FancyLights.Parameters["blurDistance"].SetValue(new Vector2(16f * (1.0f/5.0f) * shadow_smoothness / (float)Main.screenWidth, 16f * (1.0f / 5.0f) * shadow_smoothness / (float)Main.screenHeight));
				FancyLights.Parameters["lightMapTexture"].SetValue(lightmap);
				FancyLights.CurrentTechnique.Passes["CompositeFinal"].Apply();
				spriteBatch.Draw(Main.screenTargetSwap, Vector2.Zero, Color.White);
				spriteBatch.End();
			}

			orig(self);
		}

		private bool PerLightShading(ref GraphicsDevice graphicsDevice, ref SpriteBatch spriteBatch, BlendState lightBlend, Light light, bool isBlock = false)
        {
			float percievedBright = GetBrightness(light);

			if (percievedBright < cutoff)
				return false;

			int lightDistance = light.isSun ? 5000 : (int)(percievedBright * 200.0f * brightness_dist);
			graphicsDevice.SetRenderTarget(shadowcaster);
			graphicsDevice.Clear(Color.White);
			spriteBatch.Begin((SpriteSortMode)1, BlendState.Opaque);
			FancyLights.Parameters["lightCenter"].SetValue(new Vector2((float)((int)light.x - Main.screenPosition.X + Main.offScreenRange) / (float) ((Main.screenWidth) + Main.offScreenRange * 2) , (float)(light.y - (int)Main.screenPosition.Y + Main.offScreenRange) / (float) ((Main.screenHeight ) + Main.offScreenRange * 2)) );
			FancyLights.Parameters["sizeMult"].SetValue(new Vector2((float)((Main.screenWidth) + Main.offScreenRange * 2) / (float) (lightDistance), (float)((Main.screenHeight) + Main.offScreenRange * 2) / (float)(lightDistance)));
			FancyLights.Parameters["sizeBlock"].SetValue(isBlock ? new Vector2(8.0f / (float)lightDistance, 8.0f / (float)lightDistance) : new Vector2(-1, -1));
			FancyLights.CurrentTechnique.Passes["DistanceToShadowcaster"].Apply();
			spriteBatch.Draw(shadowmap, new Rectangle(0, 0, 1 << shadow_res_power, 1 << shadow_res_power), new Rectangle(light.x - (int) Main.screenPosition.X + (int)(Main.offScreenRange ) - lightDistance, light.y - (int) Main.screenPosition.Y + (int)(Main.offScreenRange ) - lightDistance, (int)(lightDistance * 2), (int)(lightDistance * 2)), Color.White);
			spriteBatch.End();

			graphicsDevice.SetRenderTarget(mappedshadows);
			graphicsDevice.Clear(Color.White);
			spriteBatch.Begin((SpriteSortMode)1, BlendState.Opaque);
			FancyLights.CurrentTechnique.Passes["DistortEquidistantAngle"].Apply();
			spriteBatch.Draw(shadowcaster, Vector2.Zero, Color.White);
			spriteBatch.End();

			int step = shadow_res_power - 2;

			while (step >= 0)
			{
				RenderTarget2D d = shadowreducer[step];
				RenderTarget2D s = (step == shadow_res_power - 2) ? mappedshadows : shadowreducer[step + 1];

				graphicsDevice.SetRenderTarget(d);
				graphicsDevice.Clear(Color.White);

				spriteBatch.Begin((SpriteSortMode)1, BlendState.Opaque);
				FancyLights.Parameters["texWidth"].SetValue(1.0f / ((float)(s.Width)));
				FancyLights.CurrentTechnique.Passes["HorizontalReduce"].Apply();
				spriteBatch.Draw(s, Vector2.Zero, Color.White);
				spriteBatch.End();

				step--;
			}

			graphicsDevice.SetRenderTarget(lightmap);
			graphicsDevice.Clear(new Color(0, 0, 0, 0));
			spriteBatch.Begin(SpriteSortMode.Immediate, lightBlend);
			spriteBatch.Draw(lightmapswap, Vector2.Zero, Color.White);
			FancyLights.Parameters["lightColor"].SetValue(light.color);
			FancyLights.Parameters["shadowMapTexture"].SetValue(shadowreducer[0]);
			FancyLights.CurrentTechnique.Passes["ApplyShadow"].Apply();
			spriteBatch.Draw(shadowcaster, new Rectangle((int)((light.x - Main.screenPosition.X - lightDistance) * Main.GameViewMatrix.Zoom.X - Main.screenWidth * (Main.GameViewMatrix.Zoom.X - 1) * 0.5) , (int)((light.y - Main.screenPosition.Y - lightDistance) * Main.GameViewMatrix.Zoom.Y - Main.screenHeight * (Main.GameViewMatrix.Zoom.Y - 1) * 0.5), (int)(lightDistance *2 * Main.GameViewMatrix.Zoom.Y), (int)(lightDistance*2 * Main.GameViewMatrix.Zoom.Y)), Color.White);
			spriteBatch.End();

			graphicsDevice.SetRenderTarget(lightmapswap);
			graphicsDevice.Clear(new Color(0,0,0,0));
			spriteBatch.Begin((SpriteSortMode)1, lightBlend);
			spriteBatch.Draw(lightmap, Vector2.Zero, Color.White);
			spriteBatch.End();

			return true;
		}

		private float GetBrightness (Light light)
        {
			//Real Luma
			//float percievedBright = 0.299f * light.color.X + 0.587f * light.color.Y + 0.114f * light.color.Z;

			//Fake Luma
			return 0.333f * light.color.X + 0.333f * light.color.Y + 0.333f * light.color.Z;
		}

		private Vector2 GetSunPos()
		{
			float bgTop = (int)((0.0 - (double)Main.screenPosition.Y) / (Main.worldSurface * 16.0 - 600.0) * 200.0);
			float height = 0;
			if (Main.dayTime)
			{
				if (Main.time < 27000.0)
				{
					height = bgTop + (float)Math.Pow(1.0 - Main.time / 54000.0 * 2.0, 2.0) * 250.0f + 180.0f;
				}
				else
				{
					height = bgTop + (float)Math.Pow((Main.time / 54000.0 - 0.5) * 2.0, 2.0) * 250.0f + 180.0f;
				}
			}
			return new Vector2((float)Main.time / (Main.dayTime ? 54000.0f : 32400.0f) * (float)(Main.screenWidth + 200f) - 100f, height + Main.sunModY);
		}

	}
	public static class TileLightScanner
	{
		public static Color GetPortalColor(int colorIndex)
		{
			return GetPortalColor(colorIndex / 2, colorIndex % 2);
		}

		public static Color GetPortalColor(int player, int portal)
		{
			Color white = Color.White;
			if (Main.netMode == 0)
			{
				white = ((portal != 0) ? Main.hslToRgb(0.52f, 1f, 0.6f) : Main.hslToRgb(0.12f, 1f, 0.5f));
			}
			else
			{
				float num = 0.08f;
				white = Main.hslToRgb((0.5f + (float)player * (num * 2f) + (float)portal * num) % 1f, 1f, 0.5f);
			}
			white.A = 66;
			return white;
		}

		public static Vector3 GetDemonTorchColor() {
			float r = 0.5f * Main.demonTorch + (1f - Main.demonTorch);
			float g = 0.3f;
			float b = Main.demonTorch + 0.5f * (1f - Main.demonTorch);
			return new Vector3(r,g,b);

		}
		public static Vector3 GetDiscoTorchColor() {
			float r = (float)Main.DiscoR / 255f;
			float g = (float)Main.DiscoG / 255f;
			float b = (float)Main.DiscoB / 255f;
			return new Vector3(r,g,b);
		}
		public static void TorchColor(int type,out float R,out float G,out float B) {
			Vector3[] TorchLight = new Vector3[22]
			{
				new Vector3(1f, 0.95f, 0.8f),
				new Vector3(0f, 0.1f, 1.3f),
				new Vector3(1f, 0.1f, 0.1f),
				new Vector3(0f, 1f, 0.1f),
				new Vector3(0.9f, 0f, 0.9f),
				new Vector3(1.4f, 1.4f, 1.4f),
				new Vector3(0.9f, 0.9f, 0f),
				GetDemonTorchColor(),
				new Vector3(1f, 1.6f, 0.5f),
				new Vector3(0.75f, 0.85f, 1.4f),
				new Vector3(1f, 0.5f, 0f),
				new Vector3(1.4f, 1.4f, 0.7f),
				new Vector3(0.75f, 1.3499999f, 1.5f),
				new Vector3(0.95f, 0.75f, 1.3f),
				GetDiscoTorchColor(),
				new Vector3(1f, 0f, 1f),
				new Vector3(1.4f, 0.85f, 0.55f),
				new Vector3(0.25f, 1.3f, 0.8f),
				new Vector3(0.95f, 0.4f, 1.4f),
				new Vector3(1.4f, 0.7f, 0.5f),
				new Vector3(1.25f, 0.6f, 1.2f),
				new Vector3(0.75f, 1.45f, 0.9f)
			};
			R = TorchLight[type].X;
			G = TorchLight[type].Y;
			B = TorchLight[type].Z;
		}

		public static void GetScreenDrawArea(Vector2 screenPosition, Vector2 offSet, out int firstTileX, out int lastTileX, out int firstTileY, out int lastTileY)
		{
			firstTileX = (int)((screenPosition.X - offSet.X) / 16f - 1f);
			lastTileX = (int)((screenPosition.X + (float)Main.screenWidth + offSet.X) / 16f) + 2;
			firstTileY = (int)((screenPosition.Y - offSet.Y) / 16f - 1f);
			lastTileY = (int)((screenPosition.Y + (float)Main.screenHeight + offSet.Y) / 16f) + 5;
			if (firstTileX < 4)
			{
				firstTileX = 4;
			}
			if (lastTileX > Main.maxTilesX - 4)
			{
				lastTileX = Main.maxTilesX - 4;
			}
			if (firstTileY < 4)
			{
				firstTileY = 4;
			}
			if (lastTileY > Main.maxTilesY - 4)
			{
				lastTileY = Main.maxTilesY - 4;
			}
		}
		public static void GetTileLight(int x, int y, out Vector3 outputColor)
		{
			outputColor = Vector3.Zero;
			Tile tile = Main.tile[x, y];
			FastRandom localRandom = FastRandom.CreateWithRandomSeed().WithModifier(x, y);
			if (y < (int)Main.worldSurface)
			{
				ApplySurfaceLight(tile, x, y, ref outputColor);
			}
			else if (y > (Main.maxTilesY - 200))
			{
				ApplyHellLight(tile, x, y, ref outputColor);
			}
			TileLightScanner.ApplyWallLight(tile, x, y, ref localRandom, ref outputColor);
			if (tile.active())
			{
				ApplyTileLight(tile, x, y, ref localRandom, ref outputColor);
			}
			ApplyLavaLight(tile, ref outputColor);
		}

		private static void ApplyLavaLight(Tile tile, ref Vector3 lightColor)
		{
			if (tile.lava() && tile.liquid > 0)
			{
				float num = 0.55f;
				num += (float)(270 - Main.mouseTextColor) / 900f;
				if (lightColor.X < num)
				{
					lightColor.X = num;
				}
				if (lightColor.Y < num)
				{
					lightColor.Y = num * 0.6f;
				}
				if (lightColor.Z < num)
				{
					lightColor.Z = num * 0.2f;
				}
			}
		}

		private static void ApplyWallLight(Tile tile, int x, int y, ref FastRandom localRandom, ref Vector3 lightColor)
		{
			float num = 0f;
			float num2 = 0f;
			float num3 = 0f;
			switch (tile.wall)
			{
			case 182:
				if (!tile.active() || !Main.tileBlockLight[tile.type])
				{
					num = 0.24f;
					num2 = 0.12f;
					num3 = 0.0899999961f;
				}
				break;
			case 33:
				if (!tile.active() || !Main.tileBlockLight[tile.type])
				{
					num = 0.0899999961f;
					num2 = 0.0525000021f;
					num3 = 0.24f;
				}
				break;
			case 174:
				if (!tile.active() || !Main.tileBlockLight[tile.type])
				{
					num = 0.2975f;
				}
				break;
			case 175:
				if (!tile.active() || !Main.tileBlockLight[tile.type])
				{
					num = 0.075f;
					num2 = 0.15f;
					num3 = 0.4f;
				}
				break;
			case 176:
				if (!tile.active() || !Main.tileBlockLight[tile.type])
				{
					num = 0.1f;
					num2 = 0.1f;
					num3 = 0.1f;
				}
				break;
			case 137:
				if (!tile.active() || !Main.tileBlockLight[tile.type])
				{
					float num4 = 0.4f;
					num4 += (float)(270 - Main.mouseTextColor) / 1500f;
					num4 += (float)localRandom.Next(0, 50) * 0.0005f;
					num = 1f * num4;
					num2 = 0.5f * num4;
					num3 = 0.1f * num4;
				}
				break;
			case 44:
				if (!tile.active() || !Main.tileBlockLight[tile.type])
				{
					num = (float)Main.DiscoR / 255f * 0.15f;
					num2 = (float)Main.DiscoG / 255f * 0.15f;
					num3 = (float)Main.DiscoB / 255f * 0.15f;
				}
				break;
			case 154:
				num = 0.6f;
				num3 = 0.6f;
				break;
			case 166:
				num = 0.6f;
				num2 = 0.6f;
				break;
			case 165:
				num3 = 0.6f;
				break;
			case 156:
				num2 = 0.6f;
				break;
			case 164:
				num = 0.6f;
				break;
			case 155:
				num = 0.6f;
				num2 = 0.6f;
				num3 = 0.6f;
				break;
			case 153:
				num = 0.6f;
				num2 = 0.3f;
				break;
			}
			if (lightColor.X < num)
			{
				lightColor.X = num;
			}
			if (lightColor.Y < num2)
			{
				lightColor.Y = num2;
			}
			if (lightColor.Z < num3)
			{
				lightColor.Z = num3;
			}
		}

		private static void ApplyTileLight(Tile tile, int x, int y, ref FastRandom localRandom, ref Vector3 lightColor)
		{
			float R = 0f;
			float G = 0f;
			float B = 0f;
			if (Main.tileLighted[tile.type])
			{
				switch (tile.type)
				{
				case 463:
					R = 0.2f;
					G = 0.4f;
					B = 0.8f;
					break;
				case 491:
					R = 0.5f;
					G = 0.4f;
					B = 0.7f;
					break;
				case 209:
					if (tile.frameX == 234 || tile.frameX == 252)
					{
						Vector3 vector2 = GetPortalColor(Main.myPlayer, 0).ToVector3() * 0.65f;
						R = vector2.X;
						G = vector2.Y;
						B = vector2.Z;
					}
					else if (tile.frameX == 306 || tile.frameX == 324)
					{
						Vector3 vector3 = GetPortalColor(Main.myPlayer, 1).ToVector3() * 0.65f;
						R = vector3.X;
						G = vector3.Y;
						B = vector3.Z;
					}
					break;
				case 415:
					R = 0.7f;
					G = 0.5f;
					B = 0.1f;
					break;
				case 500:
					R = 0.525f;
					G = 0.375f;
					B = 0.075f;
					break;
				case 416:
					R = 0f;
					G = 0.6f;
					B = 0.7f;
					break;
				case 501:
					R = 0f;
					G = 0.45f;
					B = 0.525f;
					break;
				case 417:
					R = 0.6f;
					G = 0.2f;
					B = 0.6f;
					break;
				case 502:
					R = 0.45f;
					G = 0.15f;
					B = 0.45f;
					break;
				case 418:
					R = 0.6f;
					G = 0.6f;
					B = 0.9f;
					break;
				case 503:
					R = 0.45f;
					G = 0.45f;
					B = 0.675f;
					break;
				case 390:
					R = 0.4f;
					G = 0.2f;
					B = 0.1f;
					break;
				case 597:
					switch (tile.frameX / 54)
					{
					case 0:
						R = 0.05f;
						G = 0.8f;
						B = 0.3f;
						break;
					case 1:
						R = 0.7f;
						G = 0.8f;
						B = 0.05f;
						break;
					case 2:
						R = 0.7f;
						G = 0.5f;
						B = 0.9f;
						break;
					case 3:
						R = 0.6f;
						G = 0.6f;
						B = 0.8f;
						break;
					case 4:
						R = 0.4f;
						G = 0.4f;
						B = 1.15f;
						break;
					case 5:
						R = 0.85f;
						G = 0.45f;
						B = 0.1f;
						break;
					case 6:
						R = 0.8f;
						G = 0.8f;
						B = 1f;
						break;
					case 7:
						R = 0.5f;
						G = 0.8f;
						B = 1.2f;
						break;
					}
					R *= 0.75f;
					G *= 0.75f;
					B *= 0.75f;
					break;
				case 564:
					if (tile.frameX < 36)
					{
						R = 0.05f;
						G = 0.3f;
						B = 0.55f;
					}
					break;
				case 568:
					R = 1f;
					G = 0.61f;
					B = 0.65f;
					break;
				case 569:
					R = 0.12f;
					G = 1f;
					B = 0.66f;
					break;
				case 570:
					R = 0.57f;
					G = 0.57f;
					B = 1f;
					break;
				case 580:
					R = 0.7f;
					G = 0.3f;
					B = 0.2f;
					break;
				case 391:
					R = 0.3f;
					G = 0.1f;
					B = 0.25f;
					break;
				case 381:
				case 517:
					R = 0.25f;
					G = 0.1f;
					B = 0f;
					break;
				case 534:
				case 535:
					R = 0f;
					G = 0.25f;
					B = 0f;
					break;
				case 536:
				case 537:
					R = 0f;
					G = 0.16f;
					B = 0.34f;
					break;
				case 539:
				case 540:
					R = 0.3f;
					G = 0f;
					B = 0.17f;
					break;
				case 184:
					if (tile.frameX == 110)
					{
						R = 0.25f;
						G = 0.1f;
						B = 0f;
					}
					if (tile.frameX == 132)
					{
						R = 0f;
						G = 0.25f;
						B = 0f;
					}
					if (tile.frameX == 154)
					{
						R = 0f;
						G = 0.16f;
						B = 0.34f;
					}
					if (tile.frameX == 176)
					{
						R = 0.3f;
						G = 0f;
						B = 0.17f;
					}
					break;
				case 370:
					R = 0.32f;
					G = 0.16f;
					B = 0.12f;
					break;
				case 27:
					if (tile.frameY < 36)
					{
						R = 0.3f;
						G = 0.27f;
					}
					break;
				case 336:
					R = 0.85f;
					G = 0.5f;
					B = 0.3f;
					break;
				case 340:
					R = 0.45f;
					G = 1f;
					B = 0.45f;
					break;
				case 341:
					R = 0.4f * Main.demonTorch + 0.6f * (1f - Main.demonTorch);
					G = 0.35f;
					B = 1f * Main.demonTorch + 0.6f * (1f - Main.demonTorch);
					break;
				case 342:
					R = 0.5f;
					G = 0.5f;
					B = 1.1f;
					break;
				case 343:
					R = 0.85f;
					G = 0.85f;
					B = 0.3f;
					break;
				case 344:
					R = 0.6f;
					G = 1.026f;
					B = 0.960000038f;
					break;
				case 327:
				{
					float num18 = 0.5f;
					num18 += (float)(270 - Main.mouseTextColor) / 1500f;
					num18 += (float)localRandom.Next(0, 50) * 0.0005f;
					R = 1f * num18;
					G = 0.5f * num18;
					B = 0.1f * num18;
					break;
				}
				case 316:
				case 317:
				case 318:
				{
					int num14 = x - tile.frameX / 18;
					int num15 = y - tile.frameY / 18;
					int num16 = num14 / 2 * (num15 / 3);
					num16 %= Main.cageFrames;
					bool flag4 = Main.jellyfishCageMode[tile.type - 316, num16] == 2;
					if (tile.type == 316)
					{
						if (flag4)
						{
							R = 0.2f;
							G = 0.3f;
							B = 0.8f;
						}
						else
						{
							R = 0.1f;
							G = 0.2f;
							B = 0.5f;
						}
					}
					if (tile.type == 317)
					{
						if (flag4)
						{
							R = 0.2f;
							G = 0.7f;
							B = 0.3f;
						}
						else
						{
							R = 0.05f;
							G = 0.45f;
							B = 0.1f;
						}
					}
					if (tile.type == 318)
					{
						if (flag4)
						{
							R = 0.7f;
							G = 0.2f;
							B = 0.5f;
						}
						else
						{
							R = 0.4f;
							G = 0.1f;
							B = 0.25f;
						}
					}
					break;
				}
				case 429:
				{
					int num10 = tile.frameX / 18;
					bool flag = num10 % 2 >= 1;
					bool flag2 = num10 % 4 >= 2;
					bool flag3 = num10 % 8 >= 4;
					bool num11 = num10 % 16 >= 8;
					if (flag)
					{
						R += 0.5f;
					}
					if (flag2)
					{
						G += 0.5f;
					}
					if (flag3)
					{
						B += 0.5f;
					}
					if (num11)
					{
						R += 0.2f;
						G += 0.2f;
					}
					break;
				}
				case 286:
				case 619:
					R = 0.1f;
					G = 0.2f;
					B = 0.7f;
					break;
				case 620:
				{
					Color color = new Color(230, 230, 230, 0).MultiplyRGBA(Main.hslToRgb(Main.GameUpdateCount * 0.5f % 1f, 1f, 0.5f));
					color *= 0.4f;
					R = (float)(int)color.R / 255f;
					G = (float)(int)color.G / 255f;
					B = (float)(int)color.B / 255f;
					break;
				}
				case 582:
				case 598:
					R = 0.7f;
					G = 0.2f;
					B = 0.1f;
					break;
				case 270:
					R = 0.73f;
					G = 1f;
					B = 0.41f;
					break;
				case 271:
					R = 0.45f;
					G = 0.95f;
					B = 1f;
					break;
				case 581:
					R = 1f;
					G = 0.75f;
					B = 0.5f;
					break;
				case 572:
					switch (tile.frameY / 36)
					{
					case 0:
						R = 0.9f;
						G = 0.5f;
						B = 0.7f;
						break;
					case 1:
						R = 0.7f;
						G = 0.55f;
						B = 0.96f;
						break;
					case 2:
						R = 0.45f;
						G = 0.96f;
						B = 0.95f;
						break;
					case 3:
						R = 0.5f;
						G = 0.96f;
						B = 0.62f;
						break;
					case 4:
						R = 0.47f;
						G = 0.69f;
						B = 0.95f;
						break;
					case 5:
						R = 0.92f;
						G = 0.57f;
						B = 0.51f;
						break;
					}
					break;
				case 262:
					R = 0.75f;
					B = 0.75f;
					break;
				case 263:
					R = 0.75f;
					G = 0.75f;
					break;
				case 264:
					B = 0.75f;
					break;
				case 265:
					G = 0.75f;
					break;
				case 266:
					R = 0.75f;
					break;
				case 267:
					R = 0.75f;
					G = 0.75f;
					B = 0.75f;
					break;
				case 268:
					R = 0.75f;
					G = 0.375f;
					break;
				case 237:
					R = 0.1f;
					G = 0.1f;
					break;
				case 238:
					if ((double)lightColor.X < 0.5)
					{
						lightColor.X = 0.5f;
					}
					if ((double)lightColor.Z < 0.5)
					{
						lightColor.Z = 0.5f;
					}
					break;
				case 235:
					if ((double)lightColor.X < 0.6)
					{
						lightColor.X = 0.6f;
					}
					if ((double)lightColor.Y < 0.6)
					{
						lightColor.Y = 0.6f;
					}
					break;
				case 405:
					if (tile.frameX < 54)
					{
						float num21 = (float)localRandom.Next(28, 42) * 0.005f;
						num21 += (float)(270 - Main.mouseTextColor) / 700f;
						switch (tile.frameX / 54)
						{
						case 1:
							R = 0.7f;
							G = 1f;
							B = 0.5f;
							break;
						case 2:
							R = 0.5f * Main.demonTorch + 1f * (1f - Main.demonTorch);
							G = 0.3f;
							B = 1f * Main.demonTorch + 0.5f * (1f - Main.demonTorch);
							break;
						case 3:
							R = 0.45f;
							G = 0.75f;
							B = 1f;
							break;
						case 4:
							R = 1.15f;
							G = 1.15f;
							B = 0.5f;
							break;
						case 5:
							R = (float)Main.DiscoR / 255f;
							G = (float)Main.DiscoG / 255f;
							B = (float)Main.DiscoB / 255f;
							break;
						default:
							R = 0.9f;
							G = 0.3f;
							B = 0.1f;
							break;
						}
						R += num21;
						G += num21;
						B += num21;
					}
					break;
				case 215:
					if (tile.frameY < 36)
					{
						float num20 = (float)localRandom.Next(28, 42) * 0.005f;
						num20 += (float)(270 - Main.mouseTextColor) / 700f;
						switch (tile.frameX / 54)
						{
						case 1:
							R = 0.7f;
							G = 1f;
							B = 0.5f;
							break;
						case 2:
							R = 0.5f * Main.demonTorch + 1f * (1f - Main.demonTorch);
							G = 0.3f;
							B = 1f * Main.demonTorch + 0.5f * (1f - Main.demonTorch);
							break;
						case 3:
							R = 0.45f;
							G = 0.75f;
							B = 1f;
							break;
						case 4:
							R = 1.15f;
							G = 1.15f;
							B = 0.5f;
							break;
						case 5:
							R = (float)Main.DiscoR / 255f;
							G = (float)Main.DiscoG / 255f;
							B = (float)Main.DiscoB / 255f;
							break;
						case 6:
							R = 0.75f;
							G = 1.28249991f;
							B = 1.2f;
							break;
						case 7:
							R = 0.95f;
							G = 0.65f;
							B = 1.3f;
							break;
						case 8:
							R = 1.4f;
							G = 0.85f;
							B = 0.55f;
							break;
						case 9:
							R = 0.25f;
							G = 1.3f;
							B = 0.8f;
							break;
						case 10:
							R = 0.95f;
							G = 0.4f;
							B = 1.4f;
							break;
						case 11:
							R = 1.4f;
							G = 0.7f;
							B = 0.5f;
							break;
						case 12:
							R = 1.25f;
							G = 0.6f;
							B = 1.2f;
							break;
						case 13:
							R = 0.75f;
							G = 1.45f;
							B = 0.9f;
							break;
						default:
							R = 0.9f;
							G = 0.3f;
							B = 0.1f;
							break;
						}
						R += num20;
						G += num20;
						B += num20;
					}
					break;
				case 92:
					if (tile.frameY <= 18 && tile.frameX == 0)
					{
						R = 1f;
						G = 1f;
						B = 1f;
					}
					break;
				case 592:
					if (tile.frameY > 0)
					{
						float num19 = (float)localRandom.Next(28, 42) * 0.005f;
						num19 += (float)(270 - Main.mouseTextColor) / 700f;
						R = 1.35f;
						G = 0.45f;
						B = 0.15f;
						R += num19;
						G += num19;
						B += num19;
					}
					break;
				case 593:
					if (tile.frameX < 18)
					{
						R = 0.8f;
						G = 0.3f;
						B = 0.1f;
					}
					break;
				case 594:
					if (tile.frameX < 36)
					{
						R = 0.8f;
						G = 0.3f;
						B = 0.1f;
					}
					break;
				case 548:
					if (tile.frameX / 54 >= 7)
					{
						R = 0.7f;
						G = 0.3f;
						B = 0.2f;
					}
					break;
				case 613:
				case 614:
					R = 0.7f;
					G = 0.3f;
					B = 0.2f;
					break;
				case 93:
					if (tile.frameX == 0)
					{
						switch (tile.frameY / 54)
						{
						case 1:
							R = 0.95f;
							G = 0.95f;
							B = 0.5f;
							break;
						case 2:
							R = 0.85f;
							G = 0.6f;
							B = 1f;
							break;
						case 3:
							R = 0.75f;
							G = 1f;
							B = 0.6f;
							break;
						case 4:
						case 5:
							R = 0.75f;
							G = 0.85f;
							B = 1f;
							break;
						case 6:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 7:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 9:
							R = 1f;
							G = 1f;
							B = 0.7f;
							break;
						case 10:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 12:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 13:
							R = 1f;
							G = 1f;
							B = 0.6f;
							break;
						case 14:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 18:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 19:
							R = 0.37f;
							G = 0.8f;
							B = 1f;
							break;
						case 20:
							R = 0f;
							G = 0.9f;
							B = 1f;
							break;
						case 21:
							R = 0.25f;
							G = 0.7f;
							B = 1f;
							break;
						case 23:
							R = 0.5f * Main.demonTorch + 1f * (1f - Main.demonTorch);
							G = 0.3f;
							B = 1f * Main.demonTorch + 0.5f * (1f - Main.demonTorch);
							break;
						case 24:
							R = 0.35f;
							G = 0.5f;
							B = 0.3f;
							break;
						case 25:
							R = 0.34f;
							G = 0.4f;
							B = 0.31f;
							break;
						case 26:
							R = 0.25f;
							G = 0.32f;
							B = 0.5f;
							break;
						case 29:
							R = 0.9f;
							G = 0.75f;
							B = 1f;
							break;
						case 30:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 31:
						{
							Vector3 vector7 = Main.hslToRgb(Main.demonTorch * 0.12f + 0.69f, 1f, 0.75f).ToVector3() * 1.2f;
							R = vector7.X;
							G = vector7.Y;
							B = vector7.Z;
							break;
						}
						case 32:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 33:
							R = 0.55f;
							G = 0.45f;
							B = 0.95f;
							break;
						case 34:
							R = 1f;
							G = 0.6f;
							B = 0.1f;
							break;
						case 35:
							R = 0.3f;
							G = 0.75f;
							B = 0.55f;
							break;
						case 36:
							R = 0.9f;
							G = 0.55f;
							B = 0.7f;
							break;
						case 37:
							R = 0.55f;
							G = 0.85f;
							B = 1f;
							break;
						case 38:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 39:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						default:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						}
					}
					break;
				case 96:
					if (tile.frameX >= 36)
					{
						R = 0.5f;
						G = 0.35f;
						B = 0.1f;
					}
					break;
				case 98:
					if (tile.frameY == 0)
					{
						R = 1f;
						G = 0.97f;
						B = 0.85f;
					}
					break;
				case 4:
					if (tile.frameX < 66)
					{
						TorchColor(tile.frameY / 22, out R, out G, out B);
					}
					break;
				case 372:
					if (tile.frameX == 0)
					{
						R = 0.9f;
						G = 0.1f;
						B = 0.75f;
					}
					break;
				case 33:
					if (tile.frameX == 0)
					{
						switch (tile.frameY / 22)
						{
						case 0:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 1:
							R = 0.55f;
							G = 0.85f;
							B = 0.35f;
							break;
						case 2:
							R = 0.65f;
							G = 0.95f;
							B = 0.5f;
							break;
						case 3:
							R = 0.2f;
							G = 0.75f;
							B = 1f;
							break;
						case 5:
							R = 0.85f;
							G = 0.6f;
							B = 1f;
							break;
						case 7:
						case 8:
							R = 0.75f;
							G = 0.85f;
							B = 1f;
							break;
						case 9:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 10:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 14:
							R = 1f;
							G = 1f;
							B = 0.6f;
							break;
						case 15:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 18:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 19:
							R = 0.37f;
							G = 0.8f;
							B = 1f;
							break;
						case 20:
							R = 0f;
							G = 0.9f;
							B = 1f;
							break;
						case 21:
							R = 0.25f;
							G = 0.7f;
							B = 1f;
							break;
						case 23:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 24:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 25:
							R = 0.5f * Main.demonTorch + 1f * (1f - Main.demonTorch);
							G = 0.3f;
							B = 1f * Main.demonTorch + 0.5f * (1f - Main.demonTorch);
							break;
						case 28:
							R = 0.9f;
							G = 0.75f;
							B = 1f;
							break;
						case 29:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 30:
						{
							Vector3 vector6 = Main.hslToRgb(Main.demonTorch * 0.12f + 0.69f, 1f, 0.75f).ToVector3() * 1.2f;
							R = vector6.X;
							G = vector6.Y;
							B = vector6.Z;
							break;
						}
						case 31:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 32:
							R = 0.55f;
							G = 0.45f;
							B = 0.95f;
							break;
						case 33:
							R = 1f;
							G = 0.6f;
							B = 0.1f;
							break;
						case 34:
							R = 0.3f;
							G = 0.75f;
							B = 0.55f;
							break;
						case 35:
							R = 0.9f;
							G = 0.55f;
							B = 0.7f;
							break;
						case 36:
							R = 0.55f;
							G = 0.85f;
							B = 1f;
							break;
						case 37:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 38:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						default:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						}
					}
					break;
				case 174:
					if (tile.frameX == 0)
					{
						R = 1f;
						G = 0.95f;
						B = 0.65f;
					}
					break;
				case 100:
				case 173:
					if (tile.frameX < 36)
					{
						switch (tile.frameY / 36)
						{
						case 1:
							R = 0.95f;
							G = 0.95f;
							B = 0.5f;
							break;
						case 2:
							R = 0.85f;
							G = 0.6f;
							B = 1f;
							break;
						case 3:
							R = 1f;
							G = 0.6f;
							B = 0.6f;
							break;
						case 5:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 6:
						case 7:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 8:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 9:
							R = 0.75f;
							G = 0.85f;
							B = 1f;
							break;
						case 11:
							R = 1f;
							G = 1f;
							B = 0.7f;
							break;
						case 12:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 13:
							R = 1f;
							G = 1f;
							B = 0.6f;
							break;
						case 14:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 18:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 19:
							R = 0.37f;
							G = 0.8f;
							B = 1f;
							break;
						case 20:
							R = 0f;
							G = 0.9f;
							B = 1f;
							break;
						case 21:
							R = 0.25f;
							G = 0.7f;
							B = 1f;
							break;
						case 25:
							R = 0.5f * Main.demonTorch + 1f * (1f - Main.demonTorch);
							G = 0.3f;
							B = 1f * Main.demonTorch + 0.5f * (1f - Main.demonTorch);
							break;
						case 22:
							R = 0.35f;
							G = 0.5f;
							B = 0.3f;
							break;
						case 23:
							R = 0.34f;
							G = 0.4f;
							B = 0.31f;
							break;
						case 24:
							R = 0.25f;
							G = 0.32f;
							B = 0.5f;
							break;
						case 29:
							R = 0.9f;
							G = 0.75f;
							B = 1f;
							break;
						case 30:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 31:
						{
							Vector3 vector5 = Main.hslToRgb(Main.demonTorch * 0.12f + 0.69f, 1f, 0.75f).ToVector3() * 1.2f;
							R = vector5.X;
							G = vector5.Y;
							B = vector5.Z;
							break;
						}
						case 32:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 33:
							R = 0.55f;
							G = 0.45f;
							B = 0.95f;
							break;
						case 34:
							R = 1f;
							G = 0.6f;
							B = 0.1f;
							break;
						case 35:
							R = 0.3f;
							G = 0.75f;
							B = 0.55f;
							break;
						case 36:
							R = 0.9f;
							G = 0.55f;
							B = 0.7f;
							break;
						case 37:
							R = 0.55f;
							G = 0.85f;
							B = 1f;
							break;
						case 38:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 39:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						default:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						}
					}
					break;
				case 34:
					if (tile.frameX % 108 < 54)
					{
						int num17 = tile.frameY / 54;
						switch (num17 + 37 * (tile.frameX / 108))
						{
						case 7:
							R = 0.95f;
							G = 0.95f;
							B = 0.5f;
							break;
						case 8:
							R = 0.85f;
							G = 0.6f;
							B = 1f;
							break;
						case 9:
							R = 1f;
							G = 0.6f;
							B = 0.6f;
							break;
						case 11:
						case 12:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 13:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 17:
							R = 0.75f;
							G = 0.85f;
							B = 1f;
							break;
						case 15:
							R = 1f;
							G = 1f;
							B = 0.7f;
							break;
						case 16:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 18:
							R = 1f;
							G = 1f;
							B = 0.6f;
							break;
						case 19:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 23:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 24:
							R = 0.37f;
							G = 0.8f;
							B = 1f;
							break;
						case 25:
							R = 0f;
							G = 0.9f;
							B = 1f;
							break;
						case 26:
							R = 0.25f;
							G = 0.7f;
							B = 1f;
							break;
						case 27:
							R = 0.55f;
							G = 0.85f;
							B = 0.35f;
							break;
						case 28:
							R = 0.65f;
							G = 0.95f;
							B = 0.5f;
							break;
						case 29:
							R = 0.2f;
							G = 0.75f;
							B = 1f;
							break;
						case 30:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 32:
							R = 0.5f * Main.demonTorch + 1f * (1f - Main.demonTorch);
							G = 0.3f;
							B = 1f * Main.demonTorch + 0.5f * (1f - Main.demonTorch);
							break;
						case 35:
							R = 0.9f;
							G = 0.75f;
							B = 1f;
							break;
						case 36:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 37:
						{
							Vector3 vector4 = Main.hslToRgb(Main.demonTorch * 0.12f + 0.69f, 1f, 0.75f).ToVector3() * 1.2f;
							R = vector4.X;
							G = vector4.Y;
							B = vector4.Z;
							break;
						}
						case 38:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 39:
							R = 0.55f;
							G = 0.45f;
							B = 0.95f;
							break;
						case 40:
							R = 1f;
							G = 0.6f;
							B = 0.1f;
							break;
						case 41:
							R = 0.3f;
							G = 0.75f;
							B = 0.55f;
							break;
						case 42:
							R = 0.9f;
							G = 0.55f;
							B = 0.7f;
							break;
						case 43:
							R = 0.55f;
							G = 0.85f;
							B = 1f;
							break;
						case 44:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 45:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						default:
							R = 1f;
							G = 0.95f;
							B = 0.8f;
							break;
						}
					}
					break;
				case 35:
					if (tile.frameX < 36)
					{
						R = 0.75f;
						G = 0.6f;
						B = 0.3f;
					}
					break;
				case 95:
					if (tile.frameX < 36)
					{
						R = 1f;
						G = 0.95f;
						B = 0.8f;
					}
					break;
				case 17:
				case 133:
				case 302:
					R = 0.83f;
					G = 0.6f;
					B = 0.5f;
					break;
				case 77:
					R = 0.75f;
					G = 0.45f;
					B = 0.25f;
					break;
				case 37:
					R = 0.56f;
					G = 0.43f;
					B = 0.15f;
					break;
				case 22:
				case 140:
					R = 0.12f;
					G = 0.07f;
					B = 0.32f;
					break;
				case 171:
					if (tile.frameX < 10)
					{
						x -= tile.frameX;
						y -= tile.frameY;
					}
					switch ((Main.tile[x, y].frameY & 0x3C00) >> 10)
					{
					case 1:
						R = 0.1f;
						G = 0.1f;
						B = 0.1f;
						break;
					case 2:
						R = 0.2f;
						break;
					case 3:
						G = 0.2f;
						break;
					case 4:
						B = 0.2f;
						break;
					case 5:
						R = 0.125f;
						G = 0.125f;
						break;
					case 6:
						R = 0.2f;
						G = 0.1f;
						break;
					case 7:
						R = 0.125f;
						G = 0.125f;
						break;
					case 8:
						R = 0.08f;
						G = 0.175f;
						break;
					case 9:
						G = 0.125f;
						B = 0.125f;
						break;
					case 10:
						R = 0.125f;
						B = 0.125f;
						break;
					case 11:
						R = 0.1f;
						G = 0.1f;
						B = 0.2f;
						break;
					default:
						R = (G = (B = 0f));
						break;
					}
					R *= 0.5f;
					G *= 0.5f;
					B *= 0.5f;
					break;
				case 204:
				case 347:
					R = 0.35f;
					break;
				case 42:
					if (tile.frameX == 0)
					{
						switch (tile.frameY / 36)
						{
						case 0:
							R = 0.7f;
							G = 0.65f;
							B = 0.55f;
							break;
						case 1:
							R = 0.9f;
							G = 0.75f;
							B = 0.6f;
							break;
						case 2:
							R = 0.8f;
							G = 0.6f;
							B = 0.6f;
							break;
						case 3:
							R = 0.65f;
							G = 0.5f;
							B = 0.2f;
							break;
						case 4:
							R = 0.5f;
							G = 0.7f;
							B = 0.4f;
							break;
						case 5:
							R = 0.9f;
							G = 0.4f;
							B = 0.2f;
							break;
						case 6:
							R = 0.7f;
							G = 0.75f;
							B = 0.3f;
							break;
						case 7:
						{
							float num13 = Main.demonTorch * 0.2f;
							R = 0.9f - num13;
							G = 0.9f - num13;
							B = 0.7f + num13;
							break;
						}
						case 8:
							R = 0.75f;
							G = 0.6f;
							B = 0.3f;
							break;
						case 9:
							R = 1f;
							G = 0.3f;
							B = 0.5f;
							B += Main.demonTorch * 0.2f;
							R -= Main.demonTorch * 0.1f;
							G -= Main.demonTorch * 0.2f;
							break;
						case 11:
							R = 0.85f;
							G = 0.6f;
							B = 1f;
							break;
						case 14:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 15:
						case 16:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 17:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 18:
							R = 0.75f;
							G = 0.85f;
							B = 1f;
							break;
						case 21:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 22:
							R = 1f;
							G = 1f;
							B = 0.6f;
							break;
						case 23:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 27:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 28:
							R = 0.37f;
							G = 0.8f;
							B = 1f;
							break;
						case 29:
							R = 0f;
							G = 0.9f;
							B = 1f;
							break;
						case 30:
							R = 0.25f;
							G = 0.7f;
							B = 1f;
							break;
						case 32:
							R = 0.5f * Main.demonTorch + 1f * (1f - Main.demonTorch);
							G = 0.3f;
							B = 1f * Main.demonTorch + 0.5f * (1f - Main.demonTorch);
							break;
						case 35:
							R = 0.7f;
							G = 0.6f;
							B = 0.9f;
							break;
						case 36:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 37:
						{
							Vector3 vector = Main.hslToRgb(Main.demonTorch * 0.12f + 0.69f, 1f, 0.75f).ToVector3() * 1.2f;
							R = vector.X;
							G = vector.Y;
							B = vector.Z;
							break;
						}
						case 38:
							R = 1f;
							G = 0.97f;
							B = 0.85f;
							break;
						case 39:
							R = 0.55f;
							G = 0.45f;
							B = 0.95f;
							break;
						case 40:
							R = 1f;
							G = 0.6f;
							B = 0.1f;
							break;
						case 41:
							R = 0.3f;
							G = 0.75f;
							B = 0.55f;
							break;
						case 42:
							R = 0.9f;
							G = 0.55f;
							B = 0.7f;
							break;
						case 43:
							R = 0.55f;
							G = 0.85f;
							B = 1f;
							break;
						case 44:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						case 45:
							R = 1f;
							G = 0.95f;
							B = 0.65f;
							break;
						default:
							R = 1f;
							G = 1f;
							B = 1f;
							break;
						}
					}
					break;
				case 49:
					if (tile.frameX == 0)
					{
						R = 0f;
						G = 0.35f;
						B = 0.8f;
					}
					break;
				case 519:
					if (tile.frameY == 90)
					{
						float num12 = (float)localRandom.Next(28, 42) * 0.005f;
						num12 += (float)(270 - Main.mouseTextColor) / 1000f;
						R = 0.1f;
						G = 0.2f + num12 / 2f;
						B = 0.7f + num12;
					}
					break;
				case 70:
				case 71:
				case 72:
				case 190:
				case 348:
				case 349:
				case 528:
				case 578:
					if (tile.type != 349 || tile.frameX >= 36)
					{
						float num9 = (float)localRandom.Next(28, 42) * 0.005f;
						num9 += (float)(270 - Main.mouseTextColor) / 1000f;
						R = 0f;
						G = 0.2f + num9 / 2f;
						B = 1f;
					}
					break;
				case 350:
				{
					double num8 = Main.GameUpdateCount * 0.08;
					B = (G = (R = (float)((0.0 - Math.Cos(((int)(num8 / 6.283) % 3 == 1) ? num8 : 0.0)) * 0.1 + 0.1)));
					break;
				}
				case 61:
					if (tile.frameX == 144)
					{
						float num6 = 1f + (float)(270 - Main.mouseTextColor) / 400f;
						float num7 = 0.8f - (float)(270 - Main.mouseTextColor) / 400f;
						R = 0.42f * num7;
						G = 0.81f * num6;
						B = 0.52f * num7;
					}
					break;
				case 26:
				case 31:
					if ((tile.type == 31 && tile.frameX >= 36) || (tile.type == 26 && tile.frameX >= 54))
					{
						float num4 = (float)localRandom.Next(-5, 6) * 0.0025f;
						R = 0.5f + num4 * 2f;
						G = 0.2f + num4;
						B = 0.1f;
					}
					else
					{
						float num5 = (float)localRandom.Next(-5, 6) * 0.0025f;
						R = 0.31f + num5;
						G = 0.1f;
						B = 0.44f + num5 * 2f;
					}
					break;
				case 84:
				{
					int num2 = tile.frameX / 18;
					float num3 = 0f;
					switch (num2)
					{
					case 2:
						num3 = (float)(270 - Main.mouseTextColor) / 800f;
						if (num3 > 1f)
						{
							num3 = 1f;
						}
						else if (num3 < 0f)
						{
							num3 = 0f;
						}
						R = num3 * 0.7f;
						G = num3;
						B = num3 * 0.1f;
						break;
					case 5:
						num3 = 0.9f;
						R = num3;
						G = num3 * 0.8f;
						B = num3 * 0.2f;
						break;
					case 6:
						num3 = 0.08f;
						G = num3 * 0.8f;
						B = num3;
						break;
					}
					break;
				}
				case 83:
					if (tile.frameX == 18 && !Main.dayTime)
					{
						R = 0.1f;
						G = 0.4f;
						B = 0.6f;
					}
					if (tile.frameX == 90 && !Main.raining && Main.time > 40500.0)
					{
						R = 0.9f;
						G = 0.72f;
						B = 0.18f;
					}
					break;
				case 126:
					if (tile.frameX < 36)
					{
						R = (float)Main.DiscoR / 255f;
						G = (float)Main.DiscoG / 255f;
						B = (float)Main.DiscoB / 255f;
					}
					break;
				case 125:
				{
					float num = (float)localRandom.Next(28, 42) * 0.01f;
					num += (float)(270 - Main.mouseTextColor) / 800f;
					G = (lightColor.Y = 0.3f * num);
					B = (lightColor.Z = 0.6f * num);
					break;
				}
				case 129:
					switch (tile.frameX / 18 % 3)
					{
					case 0:
						R = 0f;
						G = 0.05f;
						B = 0.25f;
						break;
					case 1:
						R = 0.2f;
						G = 0f;
						B = 0.15f;
						break;
					case 2:
						R = 0.1f;
						G = 0f;
						B = 0.2f;
						break;
					}
					break;
				case 149:
					if (tile.frameX <= 36)
					{
						switch (tile.frameX / 18)
						{
						case 0:
							R = 0.1f;
							G = 0.2f;
							B = 0.5f;
							break;
						case 1:
							R = 0.5f;
							G = 0.1f;
							B = 0.1f;
							break;
						case 2:
							R = 0.2f;
							G = 0.5f;
							B = 0.1f;
							break;
						}
						R *= (float)localRandom.Next(970, 1031) * 0.001f;
						G *= (float)localRandom.Next(970, 1031) * 0.001f;
						B *= (float)localRandom.Next(970, 1031) * 0.001f;
					}
					break;
				case 160:
					R = (float)Main.DiscoR / 255f * 0.25f;
					G = (float)Main.DiscoG / 255f * 0.25f;
					B = (float)Main.DiscoB / 255f * 0.25f;
					break;
				case 354:
					R = 0.65f;
					G = 0.35f;
					B = 0.15f;
					break;
				}
			}
			if (lightColor.X < R)
			{
				lightColor.X = R;
			}
			if (lightColor.Y < G)
			{
				lightColor.Y = G;
			}
			if (lightColor.Z < B)
			{
				lightColor.Z = B;
			}
		}

		private static void ApplySurfaceLight(Tile tile, int x, int y, ref Vector3 lightColor)
		{
			float num = 0f;
			float num2 = 0f;
			float num3 = 0f;
			float num4 = (float)(int)Main.tileColor.R / 255f;
			float num5 = (float)(int)Main.tileColor.G / 255f;
			float num6 = (float)(int)Main.tileColor.B / 255f;
			float num7 = (num4 + num5 + num6) / 3f;
			bool allowed = false;
			if (tile.type == 54 || tile.type == 541 || tile.type == 328) {
				allowed = true;
			}
			if (tile.active() && allowed)
			{
				if (lightColor.X < num7 && (Main.wallLight[tile.wall] || tile.wall == 73 || tile.wall == 227))
				{
					num = num4;
					num2 = num5;
					num3 = num6;
				}
			}
			else if ((!tile.active() || !Main.tileNoSunLight[tile.type] || ((tile.slope() != 0 || tile.halfBrick()) && Main.tile[x, y - 1].liquid == 0 && Main.tile[x, y + 1].liquid == 0 && Main.tile[x - 1, y].liquid == 0 && Main.tile[x + 1, y].liquid == 0)) && lightColor.X < num7 && (Main.wallLight[tile.wall] || tile.wall == 73 || tile.wall == 227) && tile.liquid < 200 && (!tile.halfBrick() || Main.tile[x, y - 1].liquid < 200))
			{
				num = num4;
				num2 = num5;
				num3 = num6;
			}
			if ((!tile.active() || tile.halfBrick() || !Main.tileNoSunLight[tile.type]) && ((tile.wall >= 88 && tile.wall <= 93) || tile.wall == 241) && tile.liquid < byte.MaxValue)
			{
				num = num4;
				num2 = num5;
				num3 = num6;
				int num8 = tile.wall - 88;
				if (tile.wall == 241)
				{
					num8 = 6;
				}
				switch (num8)
				{
				case 0:
					num *= 0.9f;
					num2 *= 0.15f;
					num3 *= 0.9f;
					break;
				case 1:
					num *= 0.9f;
					num2 *= 0.9f;
					num3 *= 0.15f;
					break;
				case 2:
					num *= 0.15f;
					num2 *= 0.15f;
					num3 *= 0.9f;
					break;
				case 3:
					num *= 0.15f;
					num2 *= 0.9f;
					num3 *= 0.15f;
					break;
				case 4:
					num *= 0.9f;
					num2 *= 0.15f;
					num3 *= 0.15f;
					break;
				case 5:
				{
					float num9 = 0.2f;
					float num10 = 0.7f - num9;
					num *= num10 + (float)Main.DiscoR / 255f * num9;
					num2 *= num10 + (float)Main.DiscoG / 255f * num9;
					num3 *= num10 + (float)Main.DiscoB / 255f * num9;
					break;
				}
				case 6:
					num *= 0.9f;
					num2 *= 0.5f;
					num3 *= 0f;
					break;
				}
			}
			if (lightColor.X < num)
			{
				lightColor.X = num;
			}
			if (lightColor.Y < num2)
			{
				lightColor.Y = num2;
			}
			if (lightColor.Z < num3)
			{
				lightColor.Z = num3;
			}
		}

		private static void ApplyHellLight(Tile tile, int x, int y, ref Vector3 lightColor)
		{
			float num = 0f;
			float num2 = 0f;
			float num3 = 0f;
			float num4 = 0.55f + (float)Math.Sin(Main.GameUpdateCount * 2f) * 0.08f;
			if ((!tile.active() || !Main.tileNoSunLight[tile.type] || ((tile.slope() != 0 || tile.halfBrick()) && Main.tile[x, y - 1].liquid == 0 && Main.tile[x, y + 1].liquid == 0 && Main.tile[x - 1, y].liquid == 0 && Main.tile[x + 1, y].liquid == 0)) && lightColor.X < num4 && (Main.wallLight[tile.wall] || tile.wall == 73 || tile.wall == 227) && tile.liquid < 200 && (!tile.halfBrick() || Main.tile[x, y - 1].liquid < 200))
			{
				num = num4;
				num2 = num4 * 0.6f;
				num3 = num4 * 0.2f;
			}
			if ((!tile.active() || tile.halfBrick() || !Main.tileNoSunLight[tile.type]) && tile.wall >= 88 && tile.wall <= 93 && tile.liquid < byte.MaxValue)
			{
				num = num4;
				num2 = num4 * 0.6f;
				num3 = num4 * 0.2f;
				switch (tile.wall)
				{
				case 88:
					num *= 0.9f;
					num2 *= 0.15f;
					num3 *= 0.9f;
					break;
				case 89:
					num *= 0.9f;
					num2 *= 0.9f;
					num3 *= 0.15f;
					break;
				case 90:
					num *= 0.15f;
					num2 *= 0.15f;
					num3 *= 0.9f;
					break;
				case 91:
					num *= 0.15f;
					num2 *= 0.9f;
					num3 *= 0.15f;
					break;
				case 92:
					num *= 0.9f;
					num2 *= 0.15f;
					num3 *= 0.15f;
					break;
				case 93:
				{
					float num5 = 0.2f;
					float num6 = 0.7f - num5;
					num *= num6 + (float)Main.DiscoR / 255f * num5;
					num2 *= num6 + (float)Main.DiscoG / 255f * num5;
					num3 *= num6 + (float)Main.DiscoB / 255f * num5;
					break;
				}
				}
			}
			if (lightColor.X < num)
			{
				lightColor.X = num;
			}
			if (lightColor.Y < num2)
			{
				lightColor.Y = num2;
			}
			if (lightColor.Z < num3)
			{
				lightColor.Z = num3;
			}
		}
	}
	public struct FastRandom
	{
		private const ulong RANDOM_MULTIPLIER = 25214903917uL;

		private const ulong RANDOM_ADD = 11uL;

		private const ulong RANDOM_MASK = 281474976710655uL;

		public ulong Seed { get; private set; }

		public FastRandom(ulong seed)
		{
			this = default(FastRandom);
			this.Seed = seed;
		}

		public FastRandom(int seed)
		{
			this = default(FastRandom);
			this.Seed = (ulong)seed;
		}

		public FastRandom WithModifier(ulong modifier)
		{
			return new FastRandom(FastRandom.NextSeed(modifier) ^ this.Seed);
		}

		public FastRandom WithModifier(int x, int y)
		{
			return this.WithModifier((ulong)(x + 2654435769u + ((long)y << 6)) + ((ulong)y >> 2));
		}

		public static FastRandom CreateWithRandomSeed()
		{
			return new FastRandom((ulong)Guid.NewGuid().GetHashCode());
		}

		public void NextSeed()
		{
			this.Seed = FastRandom.NextSeed(this.Seed);
		}

		private int NextBits(int bits)
		{
			this.Seed = FastRandom.NextSeed(this.Seed);
			return (int)(this.Seed >> 48 - bits);
		}

		public float NextFloat()
		{
			return (float)this.NextBits(24) * 5.96046448E-08f;
		}

		public double NextDouble()
		{
			return (float)this.NextBits(32) * 4.656613E-10f;
		}

		public int Next(int max)
		{
			if ((max & -max) == max)
			{
				return (int)((long)max * (long)this.NextBits(31) >> 31);
			}
			int num;
			int num2;
			do
			{
				num = this.NextBits(31);
				num2 = num % max;
			}
			while (num - num2 + (max - 1) < 0);
			return num2;
		}

		public int Next(int min, int max)
		{
			return this.Next(max - min) + min;
		}

		private static ulong NextSeed(ulong seed)
		{
			return (seed * 25214903917L + 11) & 0xFFFFFFFFFFFFuL;
		}
	}
	class DynamicLightsConfig : ModConfig
    {
        [Label("Use Lighting")]
        [DefaultValue(true)]
        public bool UseLighting;

        [Label("Shadow Quality")]
        [Range(6, 15)]
        [Increment(1)]
        [DefaultValue(9)]
        [Slider]
        public int ShadowRes;

        [Label("Brightness Cutoff (helps performance)")]
        [Range(0.0f, 2.0f)]
        [DefaultValue(0.49)]
        [Slider]
        public float Cutoff;

        [Label("Maximum Light Cap (0 = no Limit)")]
        [Range(0, 0xffff)]
        [DefaultValue(1024)]
        public int LightCap;

        [Label("Shadow Smoothness")]
        [Range(0.0f, 5.0f)]
        [DefaultValue(1.5f)]
        [Slider]
        public float ShadowSmooth;

        [Label("Shine Distance")]
        [Range(0.0f, 5.0f)]
        [DefaultValue(1f)]
        [Slider]
        public float DistanceMult;

        [Label("Darkest Brightness")]
        [Range(0.0f, 1.0f)]
        [DefaultValue(0.5f)]
        [Slider]
        public float DarkBrightness;

        [Label("Brightest Brightness")]
        [Range(1.0f, 10.0f)]
        [DefaultValue(2.5f)]
        [Slider]
        public float BrightBrightness;

        [Label("Brightness Falloff")]
        [Range(1.0f, 5.0f)]
        [DefaultValue(1.75f)]
        [Slider]
        public float GrowthBase;

        [Label("Increase Surface Lighting")]
        [DefaultValue(true)]
        public bool IncreaseSurfaceLight;

        [Label("Show Sun")]
        [DefaultValue(false)]
        public bool ShowSun;

        /*[Label("Brightness Scale")]
        [Range(0.0f, 10.0f)]
        [DefaultValue(1.5f)]
        [Slider]
        public float GrowthRate;*/
        public override ConfigScope Mode => ConfigScope.ClientSide;

        public override void OnChanged()
        {
            DynamicLightPort.use_lighting = UseLighting;
            DynamicLightPort.shadow_res_power = ShadowRes;
            DynamicLightPort.cutoff = Cutoff;
            DynamicLightPort.maxlights = LightCap;
            DynamicLightPort.shadow_smoothness = ShadowSmooth;
            DynamicLightPort.brightness_dist = DistanceMult;
            DynamicLightPort.dark_brightness = DarkBrightness;
            DynamicLightPort.bright_brightness = BrightBrightness;
            DynamicLightPort.brightness_growth_base = GrowthBase;
            DynamicLightPort.brightness_growth_rate = 1.5f;
            DynamicLightPort.increase_surface = IncreaseSurfaceLight;
            DynamicLightPort.show_sun = ShowSun;

            DynamicLightPort.rebuild = true;
        }
    }
}