// ReSharper disable RedundantUsingDirective
using System;

namespace Adk.Image.Dds
{
    public enum SpaceEngineersTextureKind
    {
        ColorMetal = 0,
        NormalGloss = 1,
        Add = 2,
        TransparentColor = 3,
        LegacyTransparentColor = 4
    }


    public static class SpaceEngineersTextureProfiles
    {
        public static DdsEncodeOptions CreateTexture(SpaceEngineersTextureKind kind)
        {
            DdsEncodeOptions options = new DdsEncodeOptions();
            options.GenerateMipmaps = true;
            options.MaximumMipLevels = 0;
            options.Bc7Quality = Bc7Quality.Fast;

            switch (kind)
            {
                case SpaceEngineersTextureKind.ColorMetal:
                    options.Format = DdsOutputFormat.Bc7Unorm;
                    options.MipFilter = DdsMipFilter.Box;
                    break;

                case SpaceEngineersTextureKind.NormalGloss:
                    options.Format = DdsOutputFormat.Bc7Typeless;
                    options.MipFilter = DdsMipFilter.NormalGloss;
                    break;

                case SpaceEngineersTextureKind.Add:
                    options.Format = DdsOutputFormat.Bc7Unorm;
                    options.MipFilter = DdsMipFilter.Box;
                    break;

                case SpaceEngineersTextureKind.LegacyTransparentColor:
                    options.Format = DdsOutputFormat.LegacyBgra8;
                    options.MipFilter = DdsMipFilter.Box;
                    break;

                default:
                    options.Format = DdsOutputFormat.Bc7Unorm;
                    options.MipFilter = DdsMipFilter.Box;
                    break;
            }

            return options;
        }
    }
}
