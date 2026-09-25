using System.IO;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Adk.Utils
{
    /// <summary>
    /// Resolves content using the owning mod first, with vanilla game content as a fallback.
    /// </summary>
    public static class FileContextResolver
    {
        /// <summary>
        /// Opens a binary content file using the same mod-first order as the game's content paths.
        /// </summary>
        public static BinaryReader ReadBinaryFile(string file, IMyModContext context)
        {
            BinaryReader reader;
            if (TryReadBinaryFile(file, context, out reader))
                return reader;

            string owner = context == null || context.IsBaseGame
                ? "the base game"
                : "mod '" + context.ModName + "'";

            throw new FileNotFoundException(
                "Could not resolve content file '" + file + "' from " + owner + " or vanilla game content.",
                file);
        }

        /// <summary>
        /// Tries the owning mod before falling back to vanilla game content.
        /// </summary>
        public static bool TryReadBinaryFile(string file, IMyModContext context, out BinaryReader reader)
        {
            string resolvedPath;
            return TryReadBinaryFile(file, context, out reader, out resolvedPath);
        }

        /// <summary>
        /// Tries the owning mod before falling back to vanilla game content and returns the path that was opened.
        /// </summary>
        public static bool TryReadBinaryFile(
            string file,
            IMyModContext context,
            out BinaryReader reader,
            out string resolvedPath)
        {
            reader = null;
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(file))
                return false;

            if (HasModLocation(context))
            {
                try
                {
                    reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(file, context.ModItem);
                    resolvedPath = ResolvePath(file, context.ModPath);
                    return true;
                }
                catch (FileNotFoundException)
                {
                    // The game falls back to vanilla content when the mod does not supply the path.
                }
            }

            try
            {
                reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(file);
                resolvedPath = ResolvePath(file, MyAPIGateway.Utilities.GamePaths.ContentPath);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private static string ResolvePath(string file, string root)
        {
            return Path.GetFullPath(Path.Combine(root, file));
        }

        private static bool HasModLocation(IMyModContext context)
        {
            if (context == null || context.IsBaseGame || string.IsNullOrEmpty(context.ModPath))
                return false;

            // Definition contexts created by MyDefinitionManager retain their source ModItem.
            return !string.IsNullOrEmpty(context.ModItem.Name) || context.ModItem.PublishedFileId != 0;
        }
    }
}
