All plants share one atlas texture. You can either use the png file and the separate alpha texture for the transparency cutout or you can use the tga texture. That one has the transparency information stored in its alpha channel.

I recommend turning "backface culling" off for all meshes, but for the plants, it is required.

The non-plant meshes share one BaseBolor texture. You can choose whatever roughness you like for your material and set the metalness to 0.

Some meshes, like the candles, are not combined so that you can assign e.g. an emissive material to them.

Everything is unwrapped and placed on the different colors of the BaseColor texture. If you would like more color variants, you can simply move the UV shells to another color of your liking in another 3D software like e.g. blender.