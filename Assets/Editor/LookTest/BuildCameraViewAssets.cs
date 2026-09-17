using JurassicPark.Scene;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JurassicPark.EditorTools
{
    public static class BuildCameraViewAssets
    {
        public const string ConfigPath = "Assets/Data/CameraView.asset";
        public const string ProfilePath = "Assets/Settings/LookTestProfile.asset";

        [CliCommand("build_camera_view_assets", "Create the tactical camera and readable jungle presentation settings")]
        public static string Build()
        {
            CameraViewConfig config = AssetDatabase.LoadAssetAtPath<CameraViewConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<CameraViewConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            CreateProfile(config);
            AssetDatabase.SaveAssets();
            return ConfigPath;
        }

        public static CameraViewConfig Load()
        {
            CameraViewConfig config = AssetDatabase.LoadAssetAtPath<CameraViewConfig>(ConfigPath);
            if (config == null)
            {
                Build();
                config = AssetDatabase.LoadAssetAtPath<CameraViewConfig>(ConfigPath);
            }
            return config;
        }

        public static VolumeProfile CreateProfile(CameraViewConfig config)
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            DepthOfField dof = Component<DepthOfField>(profile);
            dof.active = config.depthOfField;
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(config.blurStart);
            dof.gaussianEnd.Override(config.blurEnd);
            dof.gaussianMaxRadius.Override(config.blurRadius);
            dof.highQualitySampling.Override(true);
            Bloom bloom = Component<Bloom>(profile);
            bloom.threshold.Override(config.bloomThreshold);
            bloom.intensity.Override(config.bloomIntensity);
            bloom.scatter.Override(config.bloomScatter);
            bloom.clamp.Override(config.bloomClamp);
            bloom.tint.Override(config.bloomTint);
            Vignette vignette = Component<Vignette>(profile);
            vignette.intensity.Override(config.vignette);
            vignette.smoothness.Override(config.vignetteSoftness);
            vignette.color.Override(config.vignetteColor);
            ColorAdjustments grade = Component<ColorAdjustments>(profile);
            grade.postExposure.Override(config.exposure);
            grade.contrast.Override(config.contrast);
            grade.saturation.Override(config.saturation);
            grade.colorFilter.Override(config.colorFilter);
            // The previous diorama grade used a second warm tint, which obscured the shared jungle palette.
            if (profile.TryGet(out LiftGammaGain split)) split.active = false;
            foreach (VolumeComponent component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            return component;
        }
    }
}
