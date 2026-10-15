using UnityEditor;

namespace TheraQuest.EditorTools
{
    /// <summary>
    /// Import settings for coach characters and their animations, so they work with no manual setup:
    ///   .../Coaches/...          characters (e.g. Assets/ThirdParty/Coaches: Quaternius Ultimate Modular
    ///                            Men / Women (CC0), or Mixamo characters downloaded as "FBX for Unity", T-pose):
    ///                            Humanoid rig, no animations, and smooth normals so low-poly models don't
    ///                            look faceted. TheraQuest > Create Scene picks them up.
    ///   .../CoachAnimations/...  Mixamo animations such as "Breathing Idle" (download "Without Skin"):
    ///                            Humanoid, looping, root motion baked in place. Create Scene uses the first
    ///                            clip with "idle" in its name for the coach's resting limbs.
    /// </summary>
    public class CoachModelImporter : AssetPostprocessor
    {
        public const string Folder = "/Coaches/";
        public const string AnimationFolder = "/CoachAnimations/";

        public override uint GetVersion() => 3;

        static bool In(string path, string folder) => path.Replace('\\', '/').Contains(folder);

        void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;
            if (In(assetPath, Folder))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                // Smooth shading: blend facets that meet at less than 80°, keep sharper creases crisp
                importer.importNormals = ModelImporterNormals.Calculate;
                importer.normalCalculationMode = ModelImporterNormalCalculationMode.AreaAndAngleWeighted;
                importer.normalSmoothingAngle = 80f;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
            }
            else if (In(assetPath, AnimationFolder))
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!In(assetPath, AnimationFolder)) return;
            var importer = (ModelImporter)assetImporter;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true;              // idles loop forever
                c.lockRootRotation = true;      // "Bake into pose": the coach stays where it was placed
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
        }
    }
}
