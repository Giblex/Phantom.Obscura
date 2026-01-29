; ModuleID = 'marshal_methods.armeabi-v7a.ll'
source_filename = "marshal_methods.armeabi-v7a.ll"
target datalayout = "e-m:e-p:32:32-Fi8-i64:64-v128:64:128-a:0:32-n32-S64"
target triple = "armv7-unknown-linux-android21"

%struct.MarshalMethodName = type {
	i64, ; uint64_t id
	ptr ; char* name
}

%struct.MarshalMethodsManagedClass = type {
	i32, ; uint32_t token
	ptr ; MonoClass klass
}

@assembly_image_cache = dso_local local_unnamed_addr global [350 x ptr] zeroinitializer, align 4

; Each entry maps hash of an assembly name to an index into the `assembly_image_cache` array
@assembly_image_cache_hashes = dso_local local_unnamed_addr constant [694 x i32] [
	i32 2616222, ; 0: System.Net.NetworkInformation.dll => 0x27eb9e => 68
	i32 10166715, ; 1: System.Net.NameResolution.dll => 0x9b21bb => 67
	i32 15721112, ; 2: System.Runtime.Intrinsics.dll => 0xefe298 => 108
	i32 16619123, ; 3: Isopoh.Cryptography.Argon2.dll => 0xfd9673 => 176
	i32 32687329, ; 4: Xamarin.AndroidX.Lifecycle.Runtime => 0x1f2c4e1 => 265
	i32 34715100, ; 5: Xamarin.Google.Guava.ListenableFuture.dll => 0x211b5dc => 299
	i32 34839235, ; 6: System.IO.FileSystem.DriveInfo => 0x2139ac3 => 48
	i32 39485524, ; 7: System.Net.WebSockets.dll => 0x25a8054 => 80
	i32 42639949, ; 8: System.Threading.Thread => 0x28aa24d => 145
	i32 65960268, ; 9: Microsoft.Win32.SystemEvents.dll => 0x3ee794c => 210
	i32 66541672, ; 10: System.Diagnostics.StackTrace => 0x3f75868 => 30
	i32 67008169, ; 11: zh-Hant\Microsoft.Maui.Controls.resources => 0x3fe76a9 => 343
	i32 68219467, ; 12: System.Security.Cryptography.Primitives => 0x410f24b => 124
	i32 71521036, ; 13: KeePassLib.Standard => 0x443530c => 179
	i32 72070932, ; 14: Microsoft.Maui.Graphics.dll => 0x44bb714 => 209
	i32 82292897, ; 15: System.Runtime.CompilerServices.VisualC.dll => 0x4e7b0a1 => 102
	i32 83768722, ; 16: Microsoft.AspNetCore.Cryptography.Internal => 0x4fe3592 => 180
	i32 98325684, ; 17: Microsoft.Extensions.Diagnostics.Abstractions => 0x5dc54b4 => 191
	i32 101534019, ; 18: Xamarin.AndroidX.SlidingPaneLayout => 0x60d4943 => 283
	i32 117431740, ; 19: System.Runtime.InteropServices => 0x6ffddbc => 107
	i32 120558881, ; 20: Xamarin.AndroidX.SlidingPaneLayout.dll => 0x72f9521 => 283
	i32 122350210, ; 21: System.Threading.Channels.dll => 0x74aea82 => 139
	i32 127363243, ; 22: ICSharpCode.SharpZipLib => 0x79768ab => 215
	i32 134690465, ; 23: Xamarin.Kotlin.StdLib.Jdk7.dll => 0x80736a1 => 303
	i32 142721839, ; 24: System.Net.WebHeaderCollection => 0x881c32f => 77
	i32 149972175, ; 25: System.Security.Cryptography.Primitives.dll => 0x8f064cf => 124
	i32 159306688, ; 26: System.ComponentModel.Annotations => 0x97ed3c0 => 13
	i32 165246403, ; 27: Xamarin.AndroidX.Collection.dll => 0x9d975c3 => 239
	i32 176265551, ; 28: System.ServiceProcess => 0xa81994f => 132
	i32 182336117, ; 29: Xamarin.AndroidX.SwipeRefreshLayout.dll => 0xade3a75 => 285
	i32 184328833, ; 30: System.ValueTuple.dll => 0xafca281 => 151
	i32 195452805, ; 31: vi/Microsoft.Maui.Controls.resources.dll => 0xba65f85 => 340
	i32 199333315, ; 32: zh-HK/Microsoft.Maui.Controls.resources.dll => 0xbe195c3 => 341
	i32 205061960, ; 33: System.ComponentModel => 0xc38ff48 => 18
	i32 209399409, ; 34: Xamarin.AndroidX.Browser.dll => 0xc7b2e71 => 237
	i32 220171995, ; 35: System.Diagnostics.Debug => 0xd1f8edb => 26
	i32 230216969, ; 36: Xamarin.AndroidX.Legacy.Support.Core.Utils.dll => 0xdb8d509 => 259
	i32 230752869, ; 37: Microsoft.CSharp.dll => 0xdc10265 => 1
	i32 231409092, ; 38: System.Linq.Parallel => 0xdcb05c4 => 59
	i32 231814094, ; 39: System.Globalization => 0xdd133ce => 42
	i32 246610117, ; 40: System.Reflection.Emit.Lightweight => 0xeb2f8c5 => 91
	i32 261689757, ; 41: Xamarin.AndroidX.ConstraintLayout.dll => 0xf99119d => 242
	i32 276479776, ; 42: System.Threading.Timer.dll => 0x107abf20 => 147
	i32 276874140, ; 43: System.Runtime.WindowsRuntime.dll => 0x1080c39c => 220
	i32 278686392, ; 44: Xamarin.AndroidX.Lifecycle.LiveData.dll => 0x109c6ab8 => 261
	i32 280482487, ; 45: Xamarin.AndroidX.Interpolator => 0x10b7d2b7 => 258
	i32 280992041, ; 46: cs/Microsoft.Maui.Controls.resources.dll => 0x10bf9929 => 312
	i32 291076382, ; 47: System.IO.Pipes.AccessControl.dll => 0x1159791e => 54
	i32 298918909, ; 48: System.Net.Ping.dll => 0x11d123fd => 69
	i32 317674968, ; 49: vi\Microsoft.Maui.Controls.resources => 0x12ef55d8 => 340
	i32 318968648, ; 50: Xamarin.AndroidX.Activity.dll => 0x13031348 => 228
	i32 321597661, ; 51: System.Numerics => 0x132b30dd => 83
	i32 336156722, ; 52: ja/Microsoft.Maui.Controls.resources.dll => 0x14095832 => 325
	i32 342366114, ; 53: Xamarin.AndroidX.Lifecycle.Common => 0x146817a2 => 260
	i32 356389973, ; 54: it/Microsoft.Maui.Controls.resources.dll => 0x153e1455 => 324
	i32 360082299, ; 55: System.ServiceModel.Web => 0x15766b7b => 131
	i32 363368811, ; 56: PhantomVault.Mobile => 0x15a8916b => 0
	i32 367780167, ; 57: System.IO.Pipes => 0x15ebe147 => 55
	i32 374914964, ; 58: System.Transactions.Local => 0x1658bf94 => 149
	i32 375677976, ; 59: System.Net.ServicePoint.dll => 0x16646418 => 74
	i32 379916513, ; 60: System.Threading.Thread.dll => 0x16a510e1 => 145
	i32 385762202, ; 61: System.Memory.dll => 0x16fe439a => 62
	i32 388681140, ; 62: Yubico.DotNetPolyfills.dll => 0x172acdb4 => 308
	i32 392610295, ; 63: System.Threading.ThreadPool.dll => 0x1766c1f7 => 146
	i32 395744057, ; 64: _Microsoft.Android.Resource.Designer => 0x17969339 => 346
	i32 398680804, ; 65: Serilog.Sinks.Console => 0x17c362e4 => 213
	i32 403441872, ; 66: WindowsBase => 0x180c08d0 => 165
	i32 435591531, ; 67: sv/Microsoft.Maui.Controls.resources.dll => 0x19f6996b => 336
	i32 441335492, ; 68: Xamarin.AndroidX.ConstraintLayout.Core => 0x1a4e3ec4 => 243
	i32 442565967, ; 69: System.Collections => 0x1a61054f => 12
	i32 450948140, ; 70: Xamarin.AndroidX.Fragment.dll => 0x1ae0ec2c => 256
	i32 451504562, ; 71: System.Security.Cryptography.X509Certificates => 0x1ae969b2 => 125
	i32 456227837, ; 72: System.Web.HttpUtility.dll => 0x1b317bfd => 152
	i32 459347974, ; 73: System.Runtime.Serialization.Primitives.dll => 0x1b611806 => 113
	i32 465846621, ; 74: mscorlib => 0x1bc4415d => 166
	i32 469710990, ; 75: System.dll => 0x1bff388e => 164
	i32 476646585, ; 76: Xamarin.AndroidX.Interpolator.dll => 0x1c690cb9 => 258
	i32 486930444, ; 77: Xamarin.AndroidX.LocalBroadcastManager.dll => 0x1d05f80c => 271
	i32 498788369, ; 78: System.ObjectModel => 0x1dbae811 => 84
	i32 500358224, ; 79: id/Microsoft.Maui.Controls.resources.dll => 0x1dd2dc50 => 323
	i32 503918385, ; 80: fi/Microsoft.Maui.Controls.resources.dll => 0x1e092f31 => 317
	i32 513247710, ; 81: Microsoft.Extensions.Primitives.dll => 0x1e9789de => 203
	i32 526420162, ; 82: System.Transactions.dll => 0x1f6088c2 => 150
	i32 527452488, ; 83: Xamarin.Kotlin.StdLib.Jdk7 => 0x1f704948 => 303
	i32 530272170, ; 84: System.Linq.Queryable => 0x1f9b4faa => 60
	i32 539058512, ; 85: Microsoft.Extensions.Logging => 0x20216150 => 196
	i32 540030774, ; 86: System.IO.FileSystem.dll => 0x20303736 => 51
	i32 545304856, ; 87: System.Runtime.Extensions => 0x2080b118 => 103
	i32 545795345, ; 88: Microsoft.Extensions.Logging.Configuration.dll => 0x20882d11 => 198
	i32 546455878, ; 89: System.Runtime.Serialization.Xml => 0x20924146 => 114
	i32 549171840, ; 90: System.Globalization.Calendars => 0x20bbb280 => 40
	i32 557405415, ; 91: Jsr305Binding => 0x213954e7 => 296
	i32 569601784, ; 92: Xamarin.AndroidX.Window.Extensions.Core.Core => 0x21f36ef8 => 294
	i32 577335427, ; 93: System.Security.Cryptography.Cng => 0x22697083 => 120
	i32 578932521, ; 94: Isopoh.Cryptography.SecureArray => 0x2281cf29 => 178
	i32 592146354, ; 95: pt-BR/Microsoft.Maui.Controls.resources.dll => 0x234b6fb2 => 331
	i32 601371474, ; 96: System.IO.IsolatedStorage.dll => 0x23d83352 => 52
	i32 605376203, ; 97: System.IO.Compression.FileSystem => 0x24154ecb => 44
	i32 613668793, ; 98: System.Security.Cryptography.Algorithms => 0x2493d7b9 => 119
	i32 627609679, ; 99: Xamarin.AndroidX.CustomView => 0x2568904f => 248
	i32 627931235, ; 100: nl\Microsoft.Maui.Controls.resources => 0x256d7863 => 329
	i32 639843206, ; 101: Xamarin.AndroidX.Emoji2.ViewsHelper.dll => 0x26233b86 => 254
	i32 643868501, ; 102: System.Net => 0x2660a755 => 81
	i32 662205335, ; 103: System.Text.Encodings.Web.dll => 0x27787397 => 136
	i32 663373783, ; 104: Isopoh.Cryptography.Blake2b.dll => 0x278a47d7 => 177
	i32 663517072, ; 105: Xamarin.AndroidX.VersionedParcelable => 0x278c7790 => 290
	i32 666292255, ; 106: Xamarin.AndroidX.Arch.Core.Common.dll => 0x27b6d01f => 235
	i32 672442732, ; 107: System.Collections.Concurrent => 0x2814a96c => 8
	i32 683518922, ; 108: System.Net.Security => 0x28bdabca => 73
	i32 688181140, ; 109: ca/Microsoft.Maui.Controls.resources.dll => 0x2904cf94 => 311
	i32 690569205, ; 110: System.Xml.Linq.dll => 0x29293ff5 => 155
	i32 691348768, ; 111: Xamarin.KotlinX.Coroutines.Android.dll => 0x29352520 => 305
	i32 692151471, ; 112: Microsoft.Extensions.Logging.Console.dll => 0x294164af => 199
	i32 693804605, ; 113: System.Windows => 0x295a9e3d => 154
	i32 699345723, ; 114: System.Reflection.Emit => 0x29af2b3b => 92
	i32 700284507, ; 115: Xamarin.Jetbrains.Annotations => 0x29bd7e5b => 300
	i32 700358131, ; 116: System.IO.Compression.ZipFile => 0x29be9df3 => 45
	i32 706645707, ; 117: ko/Microsoft.Maui.Controls.resources.dll => 0x2a1e8ecb => 326
	i32 709152836, ; 118: System.Security.Cryptography.Pkcs.dll => 0x2a44d044 => 221
	i32 709557578, ; 119: de/Microsoft.Maui.Controls.resources.dll => 0x2a4afd4a => 314
	i32 720511267, ; 120: Xamarin.Kotlin.StdLib.Jdk8 => 0x2af22123 => 304
	i32 722857257, ; 121: System.Runtime.Loader.dll => 0x2b15ed29 => 109
	i32 727498632, ; 122: PhantomVault.Mobile.dll => 0x2b5cbf88 => 0
	i32 731701662, ; 123: Microsoft.Extensions.Options.ConfigurationExtensions => 0x2b9ce19e => 202
	i32 735137430, ; 124: System.Security.SecureString.dll => 0x2bd14e96 => 129
	i32 752232764, ; 125: System.Diagnostics.Contracts.dll => 0x2cd6293c => 25
	i32 755313932, ; 126: Xamarin.Android.Glide.Annotations.dll => 0x2d052d0c => 225
	i32 759454413, ; 127: System.Net.Requests => 0x2d445acd => 72
	i32 762598435, ; 128: System.IO.Pipes.dll => 0x2d745423 => 55
	i32 775507847, ; 129: System.IO.Compression => 0x2e394f87 => 46
	i32 777317022, ; 130: sk\Microsoft.Maui.Controls.resources => 0x2e54ea9e => 335
	i32 789151979, ; 131: Microsoft.Extensions.Options => 0x2f0980eb => 201
	i32 790371945, ; 132: Xamarin.AndroidX.CustomView.PoolingContainer.dll => 0x2f1c1e69 => 249
	i32 804715423, ; 133: System.Data.Common => 0x2ff6fb9f => 22
	i32 807930345, ; 134: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx.dll => 0x302809e9 => 263
	i32 809851609, ; 135: System.Drawing.Common.dll => 0x30455ad9 => 217
	i32 812630446, ; 136: Serilog => 0x306fc1ae => 212
	i32 823281589, ; 137: System.Private.Uri.dll => 0x311247b5 => 86
	i32 830298997, ; 138: System.IO.Compression.Brotli => 0x317d5b75 => 43
	i32 832635846, ; 139: System.Xml.XPath.dll => 0x31a103c6 => 160
	i32 834051424, ; 140: System.Net.Quic => 0x31b69d60 => 71
	i32 843511501, ; 141: Xamarin.AndroidX.Print => 0x3246f6cd => 276
	i32 873119928, ; 142: Microsoft.VisualBasic => 0x340ac0b8 => 3
	i32 877678880, ; 143: System.Globalization.dll => 0x34505120 => 42
	i32 878954865, ; 144: System.Net.Http.Json => 0x3463c971 => 63
	i32 904024072, ; 145: System.ComponentModel.Primitives.dll => 0x35e25008 => 16
	i32 911108515, ; 146: System.IO.MemoryMappedFiles.dll => 0x364e69a3 => 53
	i32 926902833, ; 147: tr/Microsoft.Maui.Controls.resources.dll => 0x373f6a31 => 338
	i32 928116545, ; 148: Xamarin.Google.Guava.ListenableFuture => 0x3751ef41 => 299
	i32 952186615, ; 149: System.Runtime.InteropServices.JavaScript.dll => 0x38c136f7 => 105
	i32 956575887, ; 150: Xamarin.Kotlin.StdLib.Jdk8.dll => 0x3904308f => 304
	i32 966729478, ; 151: Xamarin.Google.Crypto.Tink.Android => 0x399f1f06 => 297
	i32 967690846, ; 152: Xamarin.AndroidX.Lifecycle.Common.dll => 0x39adca5e => 260
	i32 975236339, ; 153: System.Diagnostics.Tracing => 0x3a20ecf3 => 34
	i32 975874589, ; 154: System.Xml.XDocument => 0x3a2aaa1d => 158
	i32 986514023, ; 155: System.Private.DataContractSerialization.dll => 0x3acd0267 => 85
	i32 987214855, ; 156: System.Diagnostics.Tools => 0x3ad7b407 => 32
	i32 992768348, ; 157: System.Collections.dll => 0x3b2c715c => 12
	i32 994442037, ; 158: System.IO.FileSystem => 0x3b45fb35 => 51
	i32 999186168, ; 159: Microsoft.Extensions.FileSystemGlobbing.dll => 0x3b8e5ef8 => 194
	i32 1001831731, ; 160: System.IO.UnmanagedMemoryStream.dll => 0x3bb6bd33 => 56
	i32 1012816738, ; 161: Xamarin.AndroidX.SavedState.dll => 0x3c5e5b62 => 280
	i32 1019214401, ; 162: System.Drawing => 0x3cbffa41 => 36
	i32 1028951442, ; 163: Microsoft.Extensions.DependencyInjection.Abstractions => 0x3d548d92 => 190
	i32 1029334545, ; 164: da/Microsoft.Maui.Controls.resources.dll => 0x3d5a6611 => 313
	i32 1031528504, ; 165: Xamarin.Google.ErrorProne.Annotations.dll => 0x3d7be038 => 298
	i32 1035644815, ; 166: Xamarin.AndroidX.AppCompat => 0x3dbaaf8f => 233
	i32 1036536393, ; 167: System.Drawing.Primitives.dll => 0x3dc84a49 => 35
	i32 1044663988, ; 168: System.Linq.Expressions.dll => 0x3e444eb4 => 58
	i32 1048992957, ; 169: Microsoft.Extensions.Diagnostics.Abstractions.dll => 0x3e865cbd => 191
	i32 1052210849, ; 170: Xamarin.AndroidX.Lifecycle.ViewModel.dll => 0x3eb776a1 => 267
	i32 1067306892, ; 171: GoogleGson => 0x3f9dcf8c => 175
	i32 1082857460, ; 172: System.ComponentModel.TypeConverter => 0x408b17f4 => 17
	i32 1084122840, ; 173: Xamarin.Kotlin.StdLib => 0x409e66d8 => 301
	i32 1098259244, ; 174: System => 0x41761b2c => 164
	i32 1106973742, ; 175: Microsoft.Extensions.Configuration.FileExtensions.dll => 0x41fb142e => 187
	i32 1110309514, ; 176: Microsoft.Extensions.Hosting.Abstractions => 0x422dfa8a => 195
	i32 1118262833, ; 177: ko\Microsoft.Maui.Controls.resources => 0x42a75631 => 326
	i32 1121599056, ; 178: Xamarin.AndroidX.Lifecycle.Runtime.Ktx.dll => 0x42da3e50 => 266
	i32 1127624469, ; 179: Microsoft.Extensions.Logging.Debug => 0x43362f15 => 200
	i32 1146864362, ; 180: KeePassLib.Standard.dll => 0x445bc2ea => 179
	i32 1149092582, ; 181: Xamarin.AndroidX.Window => 0x447dc2e6 => 293
	i32 1168523401, ; 182: pt\Microsoft.Maui.Controls.resources => 0x45a64089 => 332
	i32 1170634674, ; 183: System.Web.dll => 0x45c677b2 => 153
	i32 1173126369, ; 184: Microsoft.Extensions.FileProviders.Abstractions.dll => 0x45ec7ce1 => 192
	i32 1175144683, ; 185: Xamarin.AndroidX.VectorDrawable.Animated => 0x460b48eb => 289
	i32 1178241025, ; 186: Xamarin.AndroidX.Navigation.Runtime.dll => 0x463a8801 => 274
	i32 1203215381, ; 187: pl/Microsoft.Maui.Controls.resources.dll => 0x47b79c15 => 330
	i32 1204270330, ; 188: Xamarin.AndroidX.Arch.Core.Common => 0x47c7b4fa => 235
	i32 1208641965, ; 189: System.Diagnostics.Process => 0x480a69ad => 29
	i32 1214827643, ; 190: CommunityToolkit.Mvvm => 0x4868cc7b => 174
	i32 1219128291, ; 191: System.IO.IsolatedStorage => 0x48aa6be3 => 52
	i32 1234928153, ; 192: nb/Microsoft.Maui.Controls.resources.dll => 0x499b8219 => 328
	i32 1243150071, ; 193: Xamarin.AndroidX.Window.Extensions.Core.Core.dll => 0x4a18f6f7 => 294
	i32 1253011324, ; 194: Microsoft.Win32.Registry => 0x4aaf6f7c => 5
	i32 1260983243, ; 195: cs\Microsoft.Maui.Controls.resources => 0x4b2913cb => 312
	i32 1264511973, ; 196: Xamarin.AndroidX.Startup.StartupRuntime.dll => 0x4b5eebe5 => 284
	i32 1267360935, ; 197: Xamarin.AndroidX.VectorDrawable => 0x4b8a64a7 => 288
	i32 1273260888, ; 198: Xamarin.AndroidX.Collection.Ktx => 0x4be46b58 => 240
	i32 1275534314, ; 199: Xamarin.KotlinX.Coroutines.Android => 0x4c071bea => 305
	i32 1278448581, ; 200: Xamarin.AndroidX.Annotation.Jvm => 0x4c3393c5 => 232
	i32 1287445648, ; 201: Yubico.DotNetPolyfills => 0x4cbcdc90 => 308
	i32 1293217323, ; 202: Xamarin.AndroidX.DrawerLayout.dll => 0x4d14ee2b => 251
	i32 1309188875, ; 203: System.Private.DataContractSerialization => 0x4e08a30b => 85
	i32 1319884111, ; 204: System.Runtime.WindowsRuntime => 0x4eabd54f => 220
	i32 1322716291, ; 205: Xamarin.AndroidX.Window.dll => 0x4ed70c83 => 293
	i32 1322857724, ; 206: Serilog.Sinks.File.dll => 0x4ed934fc => 214
	i32 1324164729, ; 207: System.Linq => 0x4eed2679 => 61
	i32 1335329327, ; 208: System.Runtime.Serialization.Json.dll => 0x4f97822f => 112
	i32 1364015309, ; 209: System.IO => 0x514d38cd => 57
	i32 1373134921, ; 210: zh-Hans\Microsoft.Maui.Controls.resources => 0x51d86049 => 342
	i32 1376866003, ; 211: Xamarin.AndroidX.SavedState => 0x52114ed3 => 280
	i32 1379779777, ; 212: System.Resources.ResourceManager => 0x523dc4c1 => 99
	i32 1402170036, ; 213: System.Configuration.dll => 0x53936ab4 => 19
	i32 1406073936, ; 214: Xamarin.AndroidX.CoordinatorLayout => 0x53cefc50 => 244
	i32 1408764838, ; 215: System.Runtime.Serialization.Formatters.dll => 0x53f80ba6 => 111
	i32 1411638395, ; 216: System.Runtime.CompilerServices.Unsafe => 0x5423e47b => 101
	i32 1422545099, ; 217: System.Runtime.CompilerServices.VisualC => 0x54ca50cb => 102
	i32 1430672901, ; 218: ar\Microsoft.Maui.Controls.resources => 0x55465605 => 310
	i32 1434145427, ; 219: System.Runtime.Handles => 0x557b5293 => 104
	i32 1435222561, ; 220: Xamarin.Google.Crypto.Tink.Android.dll => 0x558bc221 => 297
	i32 1439761251, ; 221: System.Net.Quic.dll => 0x55d10363 => 71
	i32 1447564079, ; 222: Yubico.Core => 0x5648132f => 307
	i32 1452070440, ; 223: System.Formats.Asn1.dll => 0x568cd628 => 38
	i32 1453312822, ; 224: System.Diagnostics.Tools.dll => 0x569fcb36 => 32
	i32 1457743152, ; 225: System.Runtime.Extensions.dll => 0x56e36530 => 103
	i32 1458022317, ; 226: System.Net.Security.dll => 0x56e7a7ad => 73
	i32 1461004990, ; 227: es\Microsoft.Maui.Controls.resources => 0x57152abe => 316
	i32 1461234159, ; 228: System.Collections.Immutable.dll => 0x5718a9ef => 9
	i32 1461719063, ; 229: System.Security.Cryptography.OpenSsl => 0x57201017 => 123
	i32 1462112819, ; 230: System.IO.Compression.dll => 0x57261233 => 46
	i32 1469204771, ; 231: Xamarin.AndroidX.AppCompat.AppCompatResources => 0x57924923 => 234
	i32 1470490898, ; 232: Microsoft.Extensions.Primitives => 0x57a5e912 => 203
	i32 1479771757, ; 233: System.Collections.Immutable => 0x5833866d => 9
	i32 1480492111, ; 234: System.IO.Compression.Brotli.dll => 0x583e844f => 43
	i32 1487239319, ; 235: Microsoft.Win32.Primitives => 0x58a57897 => 4
	i32 1490025113, ; 236: Xamarin.AndroidX.SavedState.SavedState.Ktx.dll => 0x58cffa99 => 281
	i32 1493001747, ; 237: hi/Microsoft.Maui.Controls.resources.dll => 0x58fd6613 => 320
	i32 1514721132, ; 238: el/Microsoft.Maui.Controls.resources.dll => 0x5a48cf6c => 315
	i32 1521091094, ; 239: Microsoft.Extensions.FileSystemGlobbing => 0x5aaa0216 => 194
	i32 1527228472, ; 240: PhantomVault.Core => 0x5b07a838 => 345
	i32 1536373174, ; 241: System.Diagnostics.TextWriterTraceListener => 0x5b9331b6 => 31
	i32 1543031311, ; 242: System.Text.RegularExpressions.dll => 0x5bf8ca0f => 138
	i32 1543355203, ; 243: System.Reflection.Emit.dll => 0x5bfdbb43 => 92
	i32 1550322496, ; 244: System.Reflection.Extensions.dll => 0x5c680b40 => 93
	i32 1551623176, ; 245: sk/Microsoft.Maui.Controls.resources.dll => 0x5c7be408 => 335
	i32 1565862583, ; 246: System.IO.FileSystem.Primitives => 0x5d552ab7 => 49
	i32 1566207040, ; 247: System.Threading.Tasks.Dataflow.dll => 0x5d5a6c40 => 141
	i32 1573704789, ; 248: System.Runtime.Serialization.Json => 0x5dccd455 => 112
	i32 1580037396, ; 249: System.Threading.Overlapped => 0x5e2d7514 => 140
	i32 1582372066, ; 250: Xamarin.AndroidX.DocumentFile.dll => 0x5e5114e2 => 250
	i32 1592978981, ; 251: System.Runtime.Serialization.dll => 0x5ef2ee25 => 115
	i32 1597949149, ; 252: Xamarin.Google.ErrorProne.Annotations => 0x5f3ec4dd => 298
	i32 1601112923, ; 253: System.Xml.Serialization => 0x5f6f0b5b => 157
	i32 1604827217, ; 254: System.Net.WebClient => 0x5fa7b851 => 76
	i32 1618516317, ; 255: System.Net.WebSockets.Client.dll => 0x6078995d => 79
	i32 1622152042, ; 256: Xamarin.AndroidX.Loader.dll => 0x60b0136a => 270
	i32 1622358360, ; 257: System.Dynamic.Runtime => 0x60b33958 => 37
	i32 1624863272, ; 258: Xamarin.AndroidX.ViewPager2 => 0x60d97228 => 292
	i32 1625558452, ; 259: Serilog.dll => 0x60e40db4 => 212
	i32 1632842087, ; 260: Microsoft.Extensions.Configuration.Json => 0x61533167 => 188
	i32 1635184631, ; 261: Xamarin.AndroidX.Emoji2.ViewsHelper => 0x6176eff7 => 254
	i32 1636350590, ; 262: Xamarin.AndroidX.CursorAdapter => 0x6188ba7e => 247
	i32 1639515021, ; 263: System.Net.Http.dll => 0x61b9038d => 64
	i32 1639986890, ; 264: System.Text.RegularExpressions => 0x61c036ca => 138
	i32 1641389582, ; 265: System.ComponentModel.EventBasedAsync.dll => 0x61d59e0e => 15
	i32 1657153582, ; 266: System.Runtime => 0x62c6282e => 116
	i32 1658241508, ; 267: Xamarin.AndroidX.Tracing.Tracing.dll => 0x62d6c1e4 => 286
	i32 1658251792, ; 268: Xamarin.Google.Android.Material.dll => 0x62d6ea10 => 295
	i32 1670060433, ; 269: Xamarin.AndroidX.ConstraintLayout => 0x638b1991 => 242
	i32 1675553242, ; 270: System.IO.FileSystem.DriveInfo.dll => 0x63dee9da => 48
	i32 1677501392, ; 271: System.Net.Primitives.dll => 0x63fca3d0 => 70
	i32 1678508291, ; 272: System.Net.WebSockets => 0x640c0103 => 80
	i32 1679769178, ; 273: System.Security.Cryptography => 0x641f3e5a => 126
	i32 1691477237, ; 274: System.Reflection.Metadata => 0x64d1e4f5 => 94
	i32 1696967625, ; 275: System.Security.Cryptography.Csp => 0x6525abc9 => 121
	i32 1698840827, ; 276: Xamarin.Kotlin.StdLib.Common => 0x654240fb => 302
	i32 1701541528, ; 277: System.Diagnostics.Debug.dll => 0x656b7698 => 26
	i32 1720223769, ; 278: Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx => 0x66888819 => 263
	i32 1726116996, ; 279: System.Reflection.dll => 0x66e27484 => 97
	i32 1728033016, ; 280: System.Diagnostics.FileVersionInfo.dll => 0x66ffb0f8 => 28
	i32 1729485958, ; 281: Xamarin.AndroidX.CardView.dll => 0x6715dc86 => 238
	i32 1736233607, ; 282: ro/Microsoft.Maui.Controls.resources.dll => 0x677cd287 => 333
	i32 1743415430, ; 283: ca\Microsoft.Maui.Controls.resources => 0x67ea6886 => 311
	i32 1744735666, ; 284: System.Transactions.Local.dll => 0x67fe8db2 => 149
	i32 1746316138, ; 285: Mono.Android.Export => 0x6816ab6a => 169
	i32 1750313021, ; 286: Microsoft.Win32.Primitives.dll => 0x6853a83d => 4
	i32 1758240030, ; 287: System.Resources.Reader.dll => 0x68cc9d1e => 98
	i32 1763938596, ; 288: System.Diagnostics.TraceSource.dll => 0x69239124 => 33
	i32 1765942094, ; 289: System.Reflection.Extensions => 0x6942234e => 93
	i32 1766324549, ; 290: Xamarin.AndroidX.SwipeRefreshLayout => 0x6947f945 => 285
	i32 1770582343, ; 291: Microsoft.Extensions.Logging.dll => 0x6988f147 => 196
	i32 1776026572, ; 292: System.Core.dll => 0x69dc03cc => 21
	i32 1777075843, ; 293: System.Globalization.Extensions.dll => 0x69ec0683 => 41
	i32 1780572499, ; 294: Mono.Android.Runtime.dll => 0x6a216153 => 170
	i32 1782862114, ; 295: ms\Microsoft.Maui.Controls.resources => 0x6a445122 => 327
	i32 1788241197, ; 296: Xamarin.AndroidX.Fragment => 0x6a96652d => 256
	i32 1793755602, ; 297: he\Microsoft.Maui.Controls.resources => 0x6aea89d2 => 319
	i32 1808609942, ; 298: Xamarin.AndroidX.Loader => 0x6bcd3296 => 270
	i32 1813058853, ; 299: Xamarin.Kotlin.StdLib.dll => 0x6c111525 => 301
	i32 1813201214, ; 300: Xamarin.Google.Android.Material => 0x6c13413e => 295
	i32 1818569960, ; 301: Xamarin.AndroidX.Navigation.UI.dll => 0x6c652ce8 => 275
	i32 1818787751, ; 302: Microsoft.VisualBasic.Core => 0x6c687fa7 => 2
	i32 1820883333, ; 303: Microsoft.AspNetCore.Cryptography.Internal.dll => 0x6c887985 => 180
	i32 1823356469, ; 304: PhantomVault.Core.dll => 0x6cae3635 => 345
	i32 1824175904, ; 305: System.Text.Encoding.Extensions => 0x6cbab720 => 134
	i32 1824722060, ; 306: System.Runtime.Serialization.Formatters => 0x6cc30c8c => 111
	i32 1828688058, ; 307: Microsoft.Extensions.Logging.Abstractions.dll => 0x6cff90ba => 197
	i32 1842015223, ; 308: uk/Microsoft.Maui.Controls.resources.dll => 0x6dcaebf7 => 339
	i32 1847515442, ; 309: Xamarin.Android.Glide.Annotations => 0x6e1ed932 => 225
	i32 1853025655, ; 310: sv\Microsoft.Maui.Controls.resources => 0x6e72ed77 => 336
	i32 1858542181, ; 311: System.Linq.Expressions => 0x6ec71a65 => 58
	i32 1870277092, ; 312: System.Reflection.Primitives => 0x6f7a29e4 => 95
	i32 1875935024, ; 313: fr\Microsoft.Maui.Controls.resources => 0x6fd07f30 => 318
	i32 1879696579, ; 314: System.Formats.Tar.dll => 0x7009e4c3 => 39
	i32 1885316902, ; 315: Xamarin.AndroidX.Arch.Core.Runtime.dll => 0x705fa726 => 236
	i32 1888955245, ; 316: System.Diagnostics.Contracts => 0x70972b6d => 25
	i32 1889954781, ; 317: System.Reflection.Metadata.dll => 0x70a66bdd => 94
	i32 1898237753, ; 318: System.Reflection.DispatchProxy => 0x7124cf39 => 89
	i32 1900610850, ; 319: System.Resources.ResourceManager.dll => 0x71490522 => 99
	i32 1910275211, ; 320: System.Collections.NonGeneric.dll => 0x71dc7c8b => 10
	i32 1927897671, ; 321: System.CodeDom.dll => 0x72e96247 => 216
	i32 1939592360, ; 322: System.Private.Xml.Linq => 0x739bd4a8 => 87
	i32 1953680223, ; 323: Microsoft.AspNetCore.DataProtection.Abstractions => 0x7472cb5f => 182
	i32 1956758971, ; 324: System.Resources.Writer => 0x74a1c5bb => 100
	i32 1961813231, ; 325: Xamarin.AndroidX.Security.SecurityCrypto.dll => 0x74eee4ef => 282
	i32 1968388702, ; 326: Microsoft.Extensions.Configuration.dll => 0x75533a5e => 184
	i32 1983156543, ; 327: Xamarin.Kotlin.StdLib.Common.dll => 0x7634913f => 302
	i32 1985761444, ; 328: Xamarin.Android.Glide.GifDecoder => 0x765c50a4 => 227
	i32 2003115576, ; 329: el\Microsoft.Maui.Controls.resources => 0x77651e38 => 315
	i32 2011961780, ; 330: System.Buffers.dll => 0x77ec19b4 => 7
	i32 2019465201, ; 331: Xamarin.AndroidX.Lifecycle.ViewModel => 0x785e97f1 => 267
	i32 2025202353, ; 332: ar/Microsoft.Maui.Controls.resources.dll => 0x78b622b1 => 310
	i32 2031763787, ; 333: Xamarin.Android.Glide => 0x791a414b => 224
	i32 2045470958, ; 334: System.Private.Xml => 0x79eb68ee => 88
	i32 2048278909, ; 335: Microsoft.Extensions.Configuration.Binder.dll => 0x7a16417d => 186
	i32 2055257422, ; 336: Xamarin.AndroidX.Lifecycle.LiveData.Core.dll => 0x7a80bd4e => 262
	i32 2060060697, ; 337: System.Windows.dll => 0x7aca0819 => 154
	i32 2066184531, ; 338: de\Microsoft.Maui.Controls.resources => 0x7b277953 => 314
	i32 2069559498, ; 339: Isopoh.Cryptography.Argon2 => 0x7b5af8ca => 176
	i32 2070888862, ; 340: System.Diagnostics.TraceSource => 0x7b6f419e => 33
	i32 2072397586, ; 341: Microsoft.Extensions.FileProviders.Physical => 0x7b864712 => 193
	i32 2079903147, ; 342: System.Runtime.dll => 0x7bf8cdab => 116
	i32 2085039813, ; 343: System.Security.Cryptography.Xml.dll => 0x7c472ec5 => 223
	i32 2090596640, ; 344: System.Numerics.Vectors => 0x7c9bf920 => 82
	i32 2127167465, ; 345: System.Console => 0x7ec9ffe9 => 20
	i32 2142473426, ; 346: System.Collections.Specialized => 0x7fb38cd2 => 11
	i32 2143790110, ; 347: System.Xml.XmlSerializer.dll => 0x7fc7a41e => 162
	i32 2146852085, ; 348: Microsoft.VisualBasic.dll => 0x7ff65cf5 => 3
	i32 2159891885, ; 349: Microsoft.Maui => 0x80bd55ad => 207
	i32 2169148018, ; 350: hu\Microsoft.Maui.Controls.resources => 0x814a9272 => 322
	i32 2171397733, ; 351: Serilog.Sinks.Console.dll => 0x816ce665 => 213
	i32 2178612968, ; 352: System.CodeDom => 0x81dafee8 => 216
	i32 2181485124, ; 353: Serilog.Sinks.File => 0x8206d244 => 214
	i32 2181898931, ; 354: Microsoft.Extensions.Options.dll => 0x820d22b3 => 201
	i32 2182693617, ; 355: Isopoh.Cryptography.Blake2b => 0x821942f1 => 177
	i32 2192057212, ; 356: Microsoft.Extensions.Logging.Abstractions => 0x82a8237c => 197
	i32 2193016926, ; 357: System.ObjectModel.dll => 0x82b6c85e => 84
	i32 2201107256, ; 358: Xamarin.KotlinX.Coroutines.Core.Jvm.dll => 0x83323b38 => 306
	i32 2201231467, ; 359: System.Net.Http => 0x8334206b => 64
	i32 2207618523, ; 360: it\Microsoft.Maui.Controls.resources => 0x839595db => 324
	i32 2217644978, ; 361: Xamarin.AndroidX.VectorDrawable.Animated.dll => 0x842e93b2 => 289
	i32 2222056684, ; 362: System.Threading.Tasks.Parallel => 0x8471e4ec => 143
	i32 2244775296, ; 363: Xamarin.AndroidX.LocalBroadcastManager => 0x85cc8d80 => 271
	i32 2252106437, ; 364: System.Xml.Serialization.dll => 0x863c6ac5 => 157
	i32 2256313426, ; 365: System.Globalization.Extensions => 0x867c9c52 => 41
	i32 2260945011, ; 366: Yubico.YubiKey.dll => 0x86c34873 => 309
	i32 2265110946, ; 367: System.Security.AccessControl.dll => 0x8702d9a2 => 117
	i32 2266799131, ; 368: Microsoft.Extensions.Configuration.Abstractions => 0x871c9c1b => 185
	i32 2267999099, ; 369: Xamarin.Android.Glide.DiskLruCache.dll => 0x872eeb7b => 226
	i32 2270573516, ; 370: fr/Microsoft.Maui.Controls.resources.dll => 0x875633cc => 318
	i32 2279755925, ; 371: Xamarin.AndroidX.RecyclerView.dll => 0x87e25095 => 278
	i32 2291847450, ; 372: Microsoft.AspNetCore.DataProtection.dll => 0x889ad11a => 181
	i32 2293034957, ; 373: System.ServiceModel.Web.dll => 0x88acefcd => 131
	i32 2295906218, ; 374: System.Net.Sockets => 0x88d8bfaa => 75
	i32 2298471582, ; 375: System.Net.Mail => 0x88ffe49e => 66
	i32 2303942373, ; 376: nb\Microsoft.Maui.Controls.resources => 0x89535ee5 => 328
	i32 2305521784, ; 377: System.Private.CoreLib.dll => 0x896b7878 => 172
	i32 2315684594, ; 378: Xamarin.AndroidX.Annotation.dll => 0x8a068af2 => 230
	i32 2320631194, ; 379: System.Threading.Tasks.Parallel.dll => 0x8a52059a => 143
	i32 2340441535, ; 380: System.Runtime.InteropServices.RuntimeInformation.dll => 0x8b804dbf => 106
	i32 2344264397, ; 381: System.ValueTuple => 0x8bbaa2cd => 151
	i32 2353062107, ; 382: System.Net.Primitives => 0x8c40e0db => 70
	i32 2368005991, ; 383: System.Xml.ReaderWriter.dll => 0x8d24e767 => 156
	i32 2371007202, ; 384: Microsoft.Extensions.Configuration => 0x8d52b2e2 => 184
	i32 2378619854, ; 385: System.Security.Cryptography.Csp.dll => 0x8dc6dbce => 121
	i32 2383496789, ; 386: System.Security.Principal.Windows.dll => 0x8e114655 => 127
	i32 2395872292, ; 387: id\Microsoft.Maui.Controls.resources => 0x8ece1c24 => 323
	i32 2401565422, ; 388: System.Web.HttpUtility => 0x8f24faee => 152
	i32 2403452196, ; 389: Xamarin.AndroidX.Emoji2.dll => 0x8f41c524 => 253
	i32 2409680918, ; 390: NSec.Cryptography => 0x8fa0d016 => 211
	i32 2421380589, ; 391: System.Threading.Tasks.Dataflow => 0x905355ed => 141
	i32 2423080555, ; 392: Xamarin.AndroidX.Collection.Ktx.dll => 0x906d466b => 240
	i32 2427813419, ; 393: hi\Microsoft.Maui.Controls.resources => 0x90b57e2b => 320
	i32 2435356389, ; 394: System.Console.dll => 0x912896e5 => 20
	i32 2435904999, ; 395: System.ComponentModel.DataAnnotations.dll => 0x9130f5e7 => 14
	i32 2454642406, ; 396: System.Text.Encoding.dll => 0x924edee6 => 135
	i32 2458678730, ; 397: System.Net.Sockets.dll => 0x928c75ca => 75
	i32 2459001652, ; 398: System.Linq.Parallel.dll => 0x92916334 => 59
	i32 2465532216, ; 399: Xamarin.AndroidX.ConstraintLayout.Core.dll => 0x92f50938 => 243
	i32 2471841756, ; 400: netstandard.dll => 0x93554fdc => 167
	i32 2475788418, ; 401: Java.Interop.dll => 0x93918882 => 168
	i32 2480646305, ; 402: Microsoft.Maui.Controls => 0x93dba8a1 => 205
	i32 2483903535, ; 403: System.ComponentModel.EventBasedAsync => 0x940d5c2f => 15
	i32 2484371297, ; 404: System.Net.ServicePoint => 0x94147f61 => 74
	i32 2490993605, ; 405: System.AppContext.dll => 0x94798bc5 => 6
	i32 2498657740, ; 406: BouncyCastle.Cryptography.dll => 0x94ee7dcc => 173
	i32 2501346920, ; 407: System.Data.DataSetExtensions => 0x95178668 => 23
	i32 2505896520, ; 408: Xamarin.AndroidX.Lifecycle.Runtime.dll => 0x955cf248 => 265
	i32 2522472828, ; 409: Xamarin.Android.Glide.dll => 0x9659e17c => 224
	i32 2538310050, ; 410: System.Reflection.Emit.Lightweight.dll => 0x974b89a2 => 91
	i32 2550873716, ; 411: hr\Microsoft.Maui.Controls.resources => 0x980b3e74 => 321
	i32 2562349572, ; 412: Microsoft.CSharp => 0x98ba5a04 => 1
	i32 2570120770, ; 413: System.Text.Encodings.Web => 0x9930ee42 => 136
	i32 2581783588, ; 414: Xamarin.AndroidX.Lifecycle.Runtime.Ktx => 0x99e2e424 => 266
	i32 2581819634, ; 415: Xamarin.AndroidX.VectorDrawable.dll => 0x99e370f2 => 288
	i32 2585220780, ; 416: System.Text.Encoding.Extensions.dll => 0x9a1756ac => 134
	i32 2585805581, ; 417: System.Net.Ping => 0x9a20430d => 69
	i32 2589602615, ; 418: System.Threading.ThreadPool => 0x9a5a3337 => 146
	i32 2592341985, ; 419: Microsoft.Extensions.FileProviders.Abstractions => 0x9a83ffe1 => 192
	i32 2593496499, ; 420: pl\Microsoft.Maui.Controls.resources => 0x9a959db3 => 330
	i32 2605712449, ; 421: Xamarin.KotlinX.Coroutines.Core.Jvm => 0x9b500441 => 306
	i32 2615233544, ; 422: Xamarin.AndroidX.Fragment.Ktx => 0x9be14c08 => 257
	i32 2616218305, ; 423: Microsoft.Extensions.Logging.Debug.dll => 0x9bf052c1 => 200
	i32 2617129537, ; 424: System.Private.Xml.dll => 0x9bfe3a41 => 88
	i32 2618712057, ; 425: System.Reflection.TypeExtensions.dll => 0x9c165ff9 => 96
	i32 2620871830, ; 426: Xamarin.AndroidX.CursorAdapter.dll => 0x9c375496 => 247
	i32 2624644809, ; 427: Xamarin.AndroidX.DynamicAnimation => 0x9c70e6c9 => 252
	i32 2626831493, ; 428: ja\Microsoft.Maui.Controls.resources => 0x9c924485 => 325
	i32 2627185994, ; 429: System.Diagnostics.TextWriterTraceListener.dll => 0x9c97ad4a => 31
	i32 2629843544, ; 430: System.IO.Compression.ZipFile.dll => 0x9cc03a58 => 45
	i32 2633051222, ; 431: Xamarin.AndroidX.Lifecycle.LiveData => 0x9cf12c56 => 261
	i32 2660759594, ; 432: System.Security.Cryptography.ProtectedData.dll => 0x9e97f82a => 222
	i32 2663391936, ; 433: Xamarin.Android.Glide.DiskLruCache => 0x9ec022c0 => 226
	i32 2663698177, ; 434: System.Runtime.Loader => 0x9ec4cf01 => 109
	i32 2664396074, ; 435: System.Xml.XDocument.dll => 0x9ecf752a => 158
	i32 2665622720, ; 436: System.Drawing.Primitives => 0x9ee22cc0 => 35
	i32 2676780864, ; 437: System.Data.Common.dll => 0x9f8c6f40 => 22
	i32 2686887180, ; 438: System.Runtime.Serialization.Xml.dll => 0xa026a50c => 114
	i32 2693849962, ; 439: System.IO.dll => 0xa090e36a => 57
	i32 2701096212, ; 440: Xamarin.AndroidX.Tracing.Tracing => 0xa0ff7514 => 286
	i32 2715334215, ; 441: System.Threading.Tasks.dll => 0xa1d8b647 => 144
	i32 2717744543, ; 442: System.Security.Claims => 0xa1fd7d9f => 118
	i32 2719963679, ; 443: System.Security.Cryptography.Cng.dll => 0xa21f5a1f => 120
	i32 2724373263, ; 444: System.Runtime.Numerics.dll => 0xa262a30f => 110
	i32 2732626843, ; 445: Xamarin.AndroidX.Activity => 0xa2e0939b => 228
	i32 2735172069, ; 446: System.Threading.Channels => 0xa30769e5 => 139
	i32 2737747696, ; 447: Xamarin.AndroidX.AppCompat.AppCompatResources.dll => 0xa32eb6f0 => 234
	i32 2740948882, ; 448: System.IO.Pipes.AccessControl => 0xa35f8f92 => 54
	i32 2748088231, ; 449: System.Runtime.InteropServices.JavaScript => 0xa3cc7fa7 => 105
	i32 2752995522, ; 450: pt-BR\Microsoft.Maui.Controls.resources => 0xa41760c2 => 331
	i32 2758225723, ; 451: Microsoft.Maui.Controls.Xaml => 0xa4672f3b => 206
	i32 2764765095, ; 452: Microsoft.Maui.dll => 0xa4caf7a7 => 207
	i32 2765824710, ; 453: System.Text.Encoding.CodePages.dll => 0xa4db22c6 => 133
	i32 2770495804, ; 454: Xamarin.Jetbrains.Annotations.dll => 0xa522693c => 300
	i32 2778768386, ; 455: Xamarin.AndroidX.ViewPager.dll => 0xa5a0a402 => 291
	i32 2779977773, ; 456: Xamarin.AndroidX.ResourceInspection.Annotation.dll => 0xa5b3182d => 279
	i32 2785988530, ; 457: th\Microsoft.Maui.Controls.resources => 0xa60ecfb2 => 337
	i32 2788224221, ; 458: Xamarin.AndroidX.Fragment.Ktx.dll => 0xa630ecdd => 257
	i32 2795666278, ; 459: Microsoft.Win32.SystemEvents => 0xa6a27b66 => 210
	i32 2801831435, ; 460: Microsoft.Maui.Graphics => 0xa7008e0b => 209
	i32 2803228030, ; 461: System.Xml.XPath.XDocument.dll => 0xa715dd7e => 159
	i32 2806116107, ; 462: es/Microsoft.Maui.Controls.resources.dll => 0xa741ef0b => 316
	i32 2810250172, ; 463: Xamarin.AndroidX.CoordinatorLayout.dll => 0xa78103bc => 244
	i32 2819470561, ; 464: System.Xml.dll => 0xa80db4e1 => 163
	i32 2821205001, ; 465: System.ServiceProcess.dll => 0xa8282c09 => 132
	i32 2821294376, ; 466: Xamarin.AndroidX.ResourceInspection.Annotation => 0xa8298928 => 279
	i32 2824502124, ; 467: System.Xml.XmlDocument => 0xa85a7b6c => 161
	i32 2831556043, ; 468: nl/Microsoft.Maui.Controls.resources.dll => 0xa8c61dcb => 329
	i32 2838993487, ; 469: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx.dll => 0xa9379a4f => 268
	i32 2849599387, ; 470: System.Threading.Overlapped.dll => 0xa9d96f9b => 140
	i32 2853208004, ; 471: Xamarin.AndroidX.ViewPager => 0xaa107fc4 => 291
	i32 2855708567, ; 472: Xamarin.AndroidX.Transition => 0xaa36a797 => 287
	i32 2861098320, ; 473: Mono.Android.Export.dll => 0xaa88e550 => 169
	i32 2861189240, ; 474: Microsoft.Maui.Essentials => 0xaa8a4878 => 208
	i32 2867946736, ; 475: System.Security.Cryptography.ProtectedData => 0xaaf164f0 => 222
	i32 2870099610, ; 476: Xamarin.AndroidX.Activity.Ktx.dll => 0xab123e9a => 229
	i32 2875164099, ; 477: Jsr305Binding.dll => 0xab5f85c3 => 296
	i32 2875220617, ; 478: System.Globalization.Calendars.dll => 0xab606289 => 40
	i32 2884993177, ; 479: Xamarin.AndroidX.ExifInterface => 0xabf58099 => 255
	i32 2887636118, ; 480: System.Net.dll => 0xac1dd496 => 81
	i32 2898407901, ; 481: System.Management => 0xacc231dd => 219
	i32 2899753641, ; 482: System.IO.UnmanagedMemoryStream => 0xacd6baa9 => 56
	i32 2900621748, ; 483: System.Dynamic.Runtime.dll => 0xace3f9b4 => 37
	i32 2901442782, ; 484: System.Reflection => 0xacf080de => 97
	i32 2905242038, ; 485: mscorlib.dll => 0xad2a79b6 => 166
	i32 2909740682, ; 486: System.Private.CoreLib => 0xad6f1e8a => 172
	i32 2911054922, ; 487: Microsoft.Extensions.FileProviders.Physical.dll => 0xad832c4a => 193
	i32 2916838712, ; 488: Xamarin.AndroidX.ViewPager2.dll => 0xaddb6d38 => 292
	i32 2919462931, ; 489: System.Numerics.Vectors.dll => 0xae037813 => 82
	i32 2921128767, ; 490: Xamarin.AndroidX.Annotation.Experimental.dll => 0xae1ce33f => 231
	i32 2921417940, ; 491: System.Security.Cryptography.Xml => 0xae214cd4 => 223
	i32 2930358886, ; 492: Microsoft.AspNetCore.DataProtection.Abstractions.dll => 0xaea9ba66 => 182
	i32 2936416060, ; 493: System.Resources.Reader => 0xaf06273c => 98
	i32 2940926066, ; 494: System.Diagnostics.StackTrace.dll => 0xaf4af872 => 30
	i32 2942453041, ; 495: System.Xml.XPath.XDocument => 0xaf624531 => 159
	i32 2959614098, ; 496: System.ComponentModel.dll => 0xb0682092 => 18
	i32 2968338931, ; 497: System.Security.Principal.Windows => 0xb0ed41f3 => 127
	i32 2971004615, ; 498: Microsoft.Extensions.Options.ConfigurationExtensions.dll => 0xb115eec7 => 202
	i32 2972252294, ; 499: System.Security.Cryptography.Algorithms.dll => 0xb128f886 => 119
	i32 2978675010, ; 500: Xamarin.AndroidX.DrawerLayout => 0xb18af942 => 251
	i32 2987532451, ; 501: Xamarin.AndroidX.Security.SecurityCrypto => 0xb21220a3 => 282
	i32 2996846495, ; 502: Xamarin.AndroidX.Lifecycle.Process.dll => 0xb2a03f9f => 264
	i32 3016983068, ; 503: Xamarin.AndroidX.Startup.StartupRuntime => 0xb3d3821c => 284
	i32 3023353419, ; 504: WindowsBase.dll => 0xb434b64b => 165
	i32 3024354802, ; 505: Xamarin.AndroidX.Legacy.Support.Core.Utils => 0xb443fdf2 => 259
	i32 3035677308, ; 506: Isopoh.Cryptography.SecureArray.dll => 0xb4f0c27c => 178
	i32 3038032645, ; 507: _Microsoft.Android.Resource.Designer.dll => 0xb514b305 => 346
	i32 3056245963, ; 508: Xamarin.AndroidX.SavedState.SavedState.Ktx => 0xb62a9ccb => 281
	i32 3057625584, ; 509: Xamarin.AndroidX.Navigation.Common => 0xb63fa9f0 => 272
	i32 3059408633, ; 510: Mono.Android.Runtime => 0xb65adef9 => 170
	i32 3059793426, ; 511: System.ComponentModel.Primitives => 0xb660be12 => 16
	i32 3075609500, ; 512: Microsoft.AspNetCore.DataProtection.Extensions => 0xb752139c => 183
	i32 3075834255, ; 513: System.Threading.Tasks => 0xb755818f => 144
	i32 3077302341, ; 514: hu/Microsoft.Maui.Controls.resources.dll => 0xb76be845 => 322
	i32 3090735792, ; 515: System.Security.Cryptography.X509Certificates.dll => 0xb838e2b0 => 125
	i32 3099732863, ; 516: System.Security.Claims.dll => 0xb8c22b7f => 118
	i32 3099871730, ; 517: System.Formats.Cbor.dll => 0xb8c449f2 => 218
	i32 3103600923, ; 518: System.Formats.Asn1 => 0xb8fd311b => 38
	i32 3109243939, ; 519: Microsoft.Extensions.Logging.Configuration => 0xb9534c23 => 198
	i32 3111772706, ; 520: System.Runtime.Serialization => 0xb979e222 => 115
	i32 3121463068, ; 521: System.IO.FileSystem.AccessControl.dll => 0xba0dbf1c => 47
	i32 3124832203, ; 522: System.Threading.Tasks.Extensions => 0xba4127cb => 142
	i32 3132293585, ; 523: System.Security.AccessControl => 0xbab301d1 => 117
	i32 3135029042, ; 524: ICSharpCode.SharpZipLib.dll => 0xbadcbf32 => 215
	i32 3147165239, ; 525: System.Diagnostics.Tracing.dll => 0xbb95ee37 => 34
	i32 3148237826, ; 526: GoogleGson.dll => 0xbba64c02 => 175
	i32 3155681111, ; 527: Microsoft.AspNetCore.DataProtection => 0xbc17df57 => 181
	i32 3159123045, ; 528: System.Reflection.Primitives.dll => 0xbc4c6465 => 95
	i32 3160747431, ; 529: System.IO.MemoryMappedFiles => 0xbc652da7 => 53
	i32 3177652138, ; 530: GiblexVault.Security.ZK => 0xbd671faa => 344
	i32 3178803400, ; 531: Xamarin.AndroidX.Navigation.Fragment.dll => 0xbd78b0c8 => 273
	i32 3192346100, ; 532: System.Security.SecureString => 0xbe4755f4 => 129
	i32 3193515020, ; 533: System.Web => 0xbe592c0c => 153
	i32 3204380047, ; 534: System.Data.dll => 0xbefef58f => 24
	i32 3209718065, ; 535: System.Xml.XmlDocument.dll => 0xbf506931 => 161
	i32 3211777861, ; 536: Xamarin.AndroidX.DocumentFile => 0xbf6fd745 => 250
	i32 3220365878, ; 537: System.Threading => 0xbff2e236 => 148
	i32 3226221578, ; 538: System.Runtime.Handles.dll => 0xc04c3c0a => 104
	i32 3238743133, ; 539: Yubico.YubiKey => 0xc10b4c5d => 309
	i32 3251039220, ; 540: System.Reflection.DispatchProxy.dll => 0xc1c6ebf4 => 89
	i32 3258312781, ; 541: Xamarin.AndroidX.CardView => 0xc235e84d => 238
	i32 3265493905, ; 542: System.Linq.Queryable.dll => 0xc2a37b91 => 60
	i32 3265893370, ; 543: System.Threading.Tasks.Extensions.dll => 0xc2a993fa => 142
	i32 3272463733, ; 544: Microsoft.AspNetCore.DataProtection.Extensions.dll => 0xc30dd575 => 183
	i32 3277815716, ; 545: System.Resources.Writer.dll => 0xc35f7fa4 => 100
	i32 3279906254, ; 546: Microsoft.Win32.Registry.dll => 0xc37f65ce => 5
	i32 3280506390, ; 547: System.ComponentModel.Annotations.dll => 0xc3888e16 => 13
	i32 3290767353, ; 548: System.Security.Cryptography.Encoding => 0xc4251ff9 => 122
	i32 3299363146, ; 549: System.Text.Encoding => 0xc4a8494a => 135
	i32 3303498502, ; 550: System.Diagnostics.FileVersionInfo => 0xc4e76306 => 28
	i32 3305363605, ; 551: fi\Microsoft.Maui.Controls.resources => 0xc503d895 => 317
	i32 3316684772, ; 552: System.Net.Requests.dll => 0xc5b097e4 => 72
	i32 3317135071, ; 553: Xamarin.AndroidX.CustomView.dll => 0xc5b776df => 248
	i32 3317144872, ; 554: System.Data => 0xc5b79d28 => 24
	i32 3340431453, ; 555: Xamarin.AndroidX.Arch.Core.Runtime => 0xc71af05d => 236
	i32 3345895724, ; 556: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller.dll => 0xc76e512c => 277
	i32 3346324047, ; 557: Xamarin.AndroidX.Navigation.Runtime => 0xc774da4f => 274
	i32 3357674450, ; 558: ru\Microsoft.Maui.Controls.resources => 0xc8220bd2 => 334
	i32 3358260929, ; 559: System.Text.Json => 0xc82afec1 => 137
	i32 3362336904, ; 560: Xamarin.AndroidX.Activity.Ktx => 0xc8693088 => 229
	i32 3362522851, ; 561: Xamarin.AndroidX.Core => 0xc86c06e3 => 245
	i32 3366347497, ; 562: Java.Interop => 0xc8a662e9 => 168
	i32 3374999561, ; 563: Xamarin.AndroidX.RecyclerView => 0xc92a6809 => 278
	i32 3381016424, ; 564: da\Microsoft.Maui.Controls.resources => 0xc9863768 => 313
	i32 3395150330, ; 565: System.Runtime.CompilerServices.Unsafe.dll => 0xca5de1fa => 101
	i32 3403906625, ; 566: System.Security.Cryptography.OpenSsl.dll => 0xcae37e41 => 123
	i32 3405233483, ; 567: Xamarin.AndroidX.CustomView.PoolingContainer => 0xcaf7bd4b => 249
	i32 3410917174, ; 568: System.Formats.Cbor => 0xcb4e7736 => 218
	i32 3421170118, ; 569: Microsoft.Extensions.Configuration.Binder => 0xcbeae9c6 => 186
	i32 3428513518, ; 570: Microsoft.Extensions.DependencyInjection.dll => 0xcc5af6ee => 189
	i32 3429136800, ; 571: System.Xml => 0xcc6479a0 => 163
	i32 3430777524, ; 572: netstandard => 0xcc7d82b4 => 167
	i32 3441283291, ; 573: Xamarin.AndroidX.DynamicAnimation.dll => 0xcd1dd0db => 252
	i32 3445260447, ; 574: System.Formats.Tar => 0xcd5a809f => 39
	i32 3452344032, ; 575: Microsoft.Maui.Controls.Compatibility.dll => 0xcdc696e0 => 204
	i32 3463511458, ; 576: hr/Microsoft.Maui.Controls.resources.dll => 0xce70fda2 => 321
	i32 3471940407, ; 577: System.ComponentModel.TypeConverter.dll => 0xcef19b37 => 17
	i32 3476120550, ; 578: Mono.Android => 0xcf3163e6 => 171
	i32 3479583265, ; 579: ru/Microsoft.Maui.Controls.resources.dll => 0xcf663a21 => 334
	i32 3484440000, ; 580: ro\Microsoft.Maui.Controls.resources => 0xcfb055c0 => 333
	i32 3485117614, ; 581: System.Text.Json.dll => 0xcfbaacae => 137
	i32 3486566296, ; 582: System.Transactions => 0xcfd0c798 => 150
	i32 3493954962, ; 583: Xamarin.AndroidX.Concurrent.Futures.dll => 0xd0418592 => 241
	i32 3509114376, ; 584: System.Xml.Linq => 0xd128d608 => 155
	i32 3515174580, ; 585: System.Security.dll => 0xd1854eb4 => 130
	i32 3530912306, ; 586: System.Configuration => 0xd2757232 => 19
	i32 3539954161, ; 587: System.Net.HttpListener => 0xd2ff69f1 => 65
	i32 3560100363, ; 588: System.Threading.Timer => 0xd432d20b => 147
	i32 3570554715, ; 589: System.IO.FileSystem.AccessControl => 0xd4d2575b => 47
	i32 3580758918, ; 590: zh-HK\Microsoft.Maui.Controls.resources => 0xd56e0b86 => 341
	i32 3597029428, ; 591: Xamarin.Android.Glide.GifDecoder.dll => 0xd6665034 => 227
	i32 3598340787, ; 592: System.Net.WebSockets.Client => 0xd67a52b3 => 79
	i32 3605570793, ; 593: BouncyCastle.Cryptography => 0xd6e8a4e9 => 173
	i32 3608519521, ; 594: System.Linq.dll => 0xd715a361 => 61
	i32 3612435020, ; 595: System.Management.dll => 0xd751624c => 219
	i32 3624195450, ; 596: System.Runtime.InteropServices.RuntimeInformation => 0xd804d57a => 106
	i32 3627220390, ; 597: Xamarin.AndroidX.Print.dll => 0xd832fda6 => 276
	i32 3633644679, ; 598: Xamarin.AndroidX.Annotation.Experimental => 0xd8950487 => 231
	i32 3638274909, ; 599: System.IO.FileSystem.Primitives.dll => 0xd8dbab5d => 49
	i32 3641597786, ; 600: Xamarin.AndroidX.Lifecycle.LiveData.Core => 0xd90e5f5a => 262
	i32 3643446276, ; 601: tr\Microsoft.Maui.Controls.resources => 0xd92a9404 => 338
	i32 3643854240, ; 602: Xamarin.AndroidX.Navigation.Fragment => 0xd930cda0 => 273
	i32 3645089577, ; 603: System.ComponentModel.DataAnnotations => 0xd943a729 => 14
	i32 3657292374, ; 604: Microsoft.Extensions.Configuration.Abstractions.dll => 0xd9fdda56 => 185
	i32 3660523487, ; 605: System.Net.NetworkInformation => 0xda2f27df => 68
	i32 3672681054, ; 606: Mono.Android.dll => 0xdae8aa5e => 171
	i32 3682565725, ; 607: Xamarin.AndroidX.Browser => 0xdb7f7e5d => 237
	i32 3684561358, ; 608: Xamarin.AndroidX.Concurrent.Futures => 0xdb9df1ce => 241
	i32 3689375977, ; 609: System.Drawing.Common => 0xdbe768e9 => 217
	i32 3697841164, ; 610: zh-Hant/Microsoft.Maui.Controls.resources.dll => 0xdc68940c => 343
	i32 3700866549, ; 611: System.Net.WebProxy.dll => 0xdc96bdf5 => 78
	i32 3706696989, ; 612: Xamarin.AndroidX.Core.Core.Ktx.dll => 0xdcefb51d => 246
	i32 3716563718, ; 613: System.Runtime.Intrinsics => 0xdd864306 => 108
	i32 3718780102, ; 614: Xamarin.AndroidX.Annotation => 0xdda814c6 => 230
	i32 3722202641, ; 615: Microsoft.Extensions.Configuration.Json.dll => 0xdddc4e11 => 188
	i32 3724971120, ; 616: Xamarin.AndroidX.Navigation.Common.dll => 0xde068c70 => 272
	i32 3732100267, ; 617: System.Net.NameResolution => 0xde7354ab => 67
	i32 3737834244, ; 618: System.Net.Http.Json.dll => 0xdecad304 => 63
	i32 3740151212, ; 619: GiblexVault.Security.ZK.dll => 0xdeee2dac => 344
	i32 3748608112, ; 620: System.Diagnostics.DiagnosticSource => 0xdf6f3870 => 27
	i32 3751444290, ; 621: System.Xml.XPath => 0xdf9a7f42 => 160
	i32 3758424670, ; 622: Microsoft.Extensions.Configuration.FileExtensions => 0xe005025e => 187
	i32 3786282454, ; 623: Xamarin.AndroidX.Collection => 0xe1ae15d6 => 239
	i32 3792276235, ; 624: System.Collections.NonGeneric => 0xe2098b0b => 10
	i32 3800979733, ; 625: Microsoft.Maui.Controls.Compatibility => 0xe28e5915 => 204
	i32 3802395368, ; 626: System.Collections.Specialized.dll => 0xe2a3f2e8 => 11
	i32 3807198597, ; 627: System.Security.Cryptography.Pkcs => 0xe2ed3d85 => 221
	i32 3819260425, ; 628: System.Net.WebProxy => 0xe3a54a09 => 78
	i32 3823082795, ; 629: System.Security.Cryptography.dll => 0xe3df9d2b => 126
	i32 3829621856, ; 630: System.Numerics.dll => 0xe4436460 => 83
	i32 3841636137, ; 631: Microsoft.Extensions.DependencyInjection.Abstractions.dll => 0xe4fab729 => 190
	i32 3844307129, ; 632: System.Net.Mail.dll => 0xe52378b9 => 66
	i32 3849253459, ; 633: System.Runtime.InteropServices.dll => 0xe56ef253 => 107
	i32 3870376305, ; 634: System.Net.HttpListener.dll => 0xe6b14171 => 65
	i32 3873536506, ; 635: System.Security.Principal => 0xe6e179fa => 128
	i32 3875112723, ; 636: System.Security.Cryptography.Encoding.dll => 0xe6f98713 => 122
	i32 3885497537, ; 637: System.Net.WebHeaderCollection.dll => 0xe797fcc1 => 77
	i32 3885922214, ; 638: Xamarin.AndroidX.Transition.dll => 0xe79e77a6 => 287
	i32 3888767677, ; 639: Xamarin.AndroidX.ProfileInstaller.ProfileInstaller => 0xe7c9e2bd => 277
	i32 3889960447, ; 640: zh-Hans/Microsoft.Maui.Controls.resources.dll => 0xe7dc15ff => 342
	i32 3896106733, ; 641: System.Collections.Concurrent.dll => 0xe839deed => 8
	i32 3896760992, ; 642: Xamarin.AndroidX.Core.dll => 0xe843daa0 => 245
	i32 3901907137, ; 643: Microsoft.VisualBasic.Core.dll => 0xe89260c1 => 2
	i32 3920810846, ; 644: System.IO.Compression.FileSystem.dll => 0xe9b2d35e => 44
	i32 3921031405, ; 645: Xamarin.AndroidX.VersionedParcelable.dll => 0xe9b630ed => 290
	i32 3928044579, ; 646: System.Xml.ReaderWriter => 0xea213423 => 156
	i32 3928678846, ; 647: Yubico.Core.dll => 0xea2ae1be => 307
	i32 3930554604, ; 648: System.Security.Principal.dll => 0xea4780ec => 128
	i32 3931092270, ; 649: Xamarin.AndroidX.Navigation.UI => 0xea4fb52e => 275
	i32 3945713374, ; 650: System.Data.DataSetExtensions.dll => 0xeb2ecede => 23
	i32 3953953790, ; 651: System.Text.Encoding.CodePages => 0xebac8bfe => 133
	i32 3955647286, ; 652: Xamarin.AndroidX.AppCompat.dll => 0xebc66336 => 233
	i32 3959773229, ; 653: Xamarin.AndroidX.Lifecycle.Process => 0xec05582d => 264
	i32 3980434154, ; 654: th/Microsoft.Maui.Controls.resources.dll => 0xed409aea => 337
	i32 3987592930, ; 655: he/Microsoft.Maui.Controls.resources.dll => 0xedadd6e2 => 319
	i32 4003436829, ; 656: System.Diagnostics.Process.dll => 0xee9f991d => 29
	i32 4015948917, ; 657: Xamarin.AndroidX.Annotation.Jvm.dll => 0xef5e8475 => 232
	i32 4025784931, ; 658: System.Memory => 0xeff49a63 => 62
	i32 4046471985, ; 659: Microsoft.Maui.Controls.Xaml.dll => 0xf1304331 => 206
	i32 4054681211, ; 660: System.Reflection.Emit.ILGeneration => 0xf1ad867b => 90
	i32 4068434129, ; 661: System.Private.Xml.Linq.dll => 0xf27f60d1 => 87
	i32 4073602200, ; 662: System.Threading.dll => 0xf2ce3c98 => 148
	i32 4075152723, ; 663: Microsoft.Extensions.Logging.Console => 0xf2e5e553 => 199
	i32 4078967171, ; 664: Microsoft.Extensions.Hosting.Abstractions.dll => 0xf3201983 => 195
	i32 4094352644, ; 665: Microsoft.Maui.Essentials.dll => 0xf40add04 => 208
	i32 4099507663, ; 666: System.Drawing.dll => 0xf45985cf => 36
	i32 4100113165, ; 667: System.Private.Uri => 0xf462c30d => 86
	i32 4101593132, ; 668: Xamarin.AndroidX.Emoji2 => 0xf479582c => 253
	i32 4102112229, ; 669: pt/Microsoft.Maui.Controls.resources.dll => 0xf48143e5 => 332
	i32 4125707920, ; 670: ms/Microsoft.Maui.Controls.resources.dll => 0xf5e94e90 => 327
	i32 4126470640, ; 671: Microsoft.Extensions.DependencyInjection => 0xf5f4f1f0 => 189
	i32 4127667938, ; 672: System.IO.FileSystem.Watcher => 0xf60736e2 => 50
	i32 4130442656, ; 673: System.AppContext => 0xf6318da0 => 6
	i32 4147896353, ; 674: System.Reflection.Emit.ILGeneration.dll => 0xf73be021 => 90
	i32 4150914736, ; 675: uk\Microsoft.Maui.Controls.resources => 0xf769eeb0 => 339
	i32 4151237749, ; 676: System.Core => 0xf76edc75 => 21
	i32 4159265925, ; 677: System.Xml.XmlSerializer => 0xf7e95c85 => 162
	i32 4161255271, ; 678: System.Reflection.TypeExtensions => 0xf807b767 => 96
	i32 4164802419, ; 679: System.IO.FileSystem.Watcher.dll => 0xf83dd773 => 50
	i32 4181436372, ; 680: System.Runtime.Serialization.Primitives => 0xf93ba7d4 => 113
	i32 4182413190, ; 681: Xamarin.AndroidX.Lifecycle.ViewModelSavedState.dll => 0xf94a8f86 => 269
	i32 4185676441, ; 682: System.Security => 0xf97c5a99 => 130
	i32 4196529839, ; 683: System.Net.WebClient.dll => 0xfa21f6af => 76
	i32 4213026141, ; 684: System.Diagnostics.DiagnosticSource.dll => 0xfb1dad5d => 27
	i32 4256097574, ; 685: Xamarin.AndroidX.Core.Core.Ktx => 0xfdaee526 => 246
	i32 4258378803, ; 686: Xamarin.AndroidX.Lifecycle.ViewModel.Ktx => 0xfdd1b433 => 268
	i32 4260525087, ; 687: System.Buffers => 0xfdf2741f => 7
	i32 4268112974, ; 688: NSec.Cryptography.dll => 0xfe663c4e => 211
	i32 4271975918, ; 689: Microsoft.Maui.Controls.dll => 0xfea12dee => 205
	i32 4274623895, ; 690: CommunityToolkit.Mvvm.dll => 0xfec99597 => 174
	i32 4274976490, ; 691: System.Runtime.Numerics => 0xfecef6ea => 110
	i32 4292120959, ; 692: Xamarin.AndroidX.Lifecycle.ViewModelSavedState => 0xffd4917f => 269
	i32 4294763496 ; 693: Xamarin.AndroidX.ExifInterface.dll => 0xfffce3e8 => 255
], align 4

@assembly_image_cache_indices = dso_local local_unnamed_addr constant [694 x i32] [
	i32 68, ; 0
	i32 67, ; 1
	i32 108, ; 2
	i32 176, ; 3
	i32 265, ; 4
	i32 299, ; 5
	i32 48, ; 6
	i32 80, ; 7
	i32 145, ; 8
	i32 210, ; 9
	i32 30, ; 10
	i32 343, ; 11
	i32 124, ; 12
	i32 179, ; 13
	i32 209, ; 14
	i32 102, ; 15
	i32 180, ; 16
	i32 191, ; 17
	i32 283, ; 18
	i32 107, ; 19
	i32 283, ; 20
	i32 139, ; 21
	i32 215, ; 22
	i32 303, ; 23
	i32 77, ; 24
	i32 124, ; 25
	i32 13, ; 26
	i32 239, ; 27
	i32 132, ; 28
	i32 285, ; 29
	i32 151, ; 30
	i32 340, ; 31
	i32 341, ; 32
	i32 18, ; 33
	i32 237, ; 34
	i32 26, ; 35
	i32 259, ; 36
	i32 1, ; 37
	i32 59, ; 38
	i32 42, ; 39
	i32 91, ; 40
	i32 242, ; 41
	i32 147, ; 42
	i32 220, ; 43
	i32 261, ; 44
	i32 258, ; 45
	i32 312, ; 46
	i32 54, ; 47
	i32 69, ; 48
	i32 340, ; 49
	i32 228, ; 50
	i32 83, ; 51
	i32 325, ; 52
	i32 260, ; 53
	i32 324, ; 54
	i32 131, ; 55
	i32 0, ; 56
	i32 55, ; 57
	i32 149, ; 58
	i32 74, ; 59
	i32 145, ; 60
	i32 62, ; 61
	i32 308, ; 62
	i32 146, ; 63
	i32 346, ; 64
	i32 213, ; 65
	i32 165, ; 66
	i32 336, ; 67
	i32 243, ; 68
	i32 12, ; 69
	i32 256, ; 70
	i32 125, ; 71
	i32 152, ; 72
	i32 113, ; 73
	i32 166, ; 74
	i32 164, ; 75
	i32 258, ; 76
	i32 271, ; 77
	i32 84, ; 78
	i32 323, ; 79
	i32 317, ; 80
	i32 203, ; 81
	i32 150, ; 82
	i32 303, ; 83
	i32 60, ; 84
	i32 196, ; 85
	i32 51, ; 86
	i32 103, ; 87
	i32 198, ; 88
	i32 114, ; 89
	i32 40, ; 90
	i32 296, ; 91
	i32 294, ; 92
	i32 120, ; 93
	i32 178, ; 94
	i32 331, ; 95
	i32 52, ; 96
	i32 44, ; 97
	i32 119, ; 98
	i32 248, ; 99
	i32 329, ; 100
	i32 254, ; 101
	i32 81, ; 102
	i32 136, ; 103
	i32 177, ; 104
	i32 290, ; 105
	i32 235, ; 106
	i32 8, ; 107
	i32 73, ; 108
	i32 311, ; 109
	i32 155, ; 110
	i32 305, ; 111
	i32 199, ; 112
	i32 154, ; 113
	i32 92, ; 114
	i32 300, ; 115
	i32 45, ; 116
	i32 326, ; 117
	i32 221, ; 118
	i32 314, ; 119
	i32 304, ; 120
	i32 109, ; 121
	i32 0, ; 122
	i32 202, ; 123
	i32 129, ; 124
	i32 25, ; 125
	i32 225, ; 126
	i32 72, ; 127
	i32 55, ; 128
	i32 46, ; 129
	i32 335, ; 130
	i32 201, ; 131
	i32 249, ; 132
	i32 22, ; 133
	i32 263, ; 134
	i32 217, ; 135
	i32 212, ; 136
	i32 86, ; 137
	i32 43, ; 138
	i32 160, ; 139
	i32 71, ; 140
	i32 276, ; 141
	i32 3, ; 142
	i32 42, ; 143
	i32 63, ; 144
	i32 16, ; 145
	i32 53, ; 146
	i32 338, ; 147
	i32 299, ; 148
	i32 105, ; 149
	i32 304, ; 150
	i32 297, ; 151
	i32 260, ; 152
	i32 34, ; 153
	i32 158, ; 154
	i32 85, ; 155
	i32 32, ; 156
	i32 12, ; 157
	i32 51, ; 158
	i32 194, ; 159
	i32 56, ; 160
	i32 280, ; 161
	i32 36, ; 162
	i32 190, ; 163
	i32 313, ; 164
	i32 298, ; 165
	i32 233, ; 166
	i32 35, ; 167
	i32 58, ; 168
	i32 191, ; 169
	i32 267, ; 170
	i32 175, ; 171
	i32 17, ; 172
	i32 301, ; 173
	i32 164, ; 174
	i32 187, ; 175
	i32 195, ; 176
	i32 326, ; 177
	i32 266, ; 178
	i32 200, ; 179
	i32 179, ; 180
	i32 293, ; 181
	i32 332, ; 182
	i32 153, ; 183
	i32 192, ; 184
	i32 289, ; 185
	i32 274, ; 186
	i32 330, ; 187
	i32 235, ; 188
	i32 29, ; 189
	i32 174, ; 190
	i32 52, ; 191
	i32 328, ; 192
	i32 294, ; 193
	i32 5, ; 194
	i32 312, ; 195
	i32 284, ; 196
	i32 288, ; 197
	i32 240, ; 198
	i32 305, ; 199
	i32 232, ; 200
	i32 308, ; 201
	i32 251, ; 202
	i32 85, ; 203
	i32 220, ; 204
	i32 293, ; 205
	i32 214, ; 206
	i32 61, ; 207
	i32 112, ; 208
	i32 57, ; 209
	i32 342, ; 210
	i32 280, ; 211
	i32 99, ; 212
	i32 19, ; 213
	i32 244, ; 214
	i32 111, ; 215
	i32 101, ; 216
	i32 102, ; 217
	i32 310, ; 218
	i32 104, ; 219
	i32 297, ; 220
	i32 71, ; 221
	i32 307, ; 222
	i32 38, ; 223
	i32 32, ; 224
	i32 103, ; 225
	i32 73, ; 226
	i32 316, ; 227
	i32 9, ; 228
	i32 123, ; 229
	i32 46, ; 230
	i32 234, ; 231
	i32 203, ; 232
	i32 9, ; 233
	i32 43, ; 234
	i32 4, ; 235
	i32 281, ; 236
	i32 320, ; 237
	i32 315, ; 238
	i32 194, ; 239
	i32 345, ; 240
	i32 31, ; 241
	i32 138, ; 242
	i32 92, ; 243
	i32 93, ; 244
	i32 335, ; 245
	i32 49, ; 246
	i32 141, ; 247
	i32 112, ; 248
	i32 140, ; 249
	i32 250, ; 250
	i32 115, ; 251
	i32 298, ; 252
	i32 157, ; 253
	i32 76, ; 254
	i32 79, ; 255
	i32 270, ; 256
	i32 37, ; 257
	i32 292, ; 258
	i32 212, ; 259
	i32 188, ; 260
	i32 254, ; 261
	i32 247, ; 262
	i32 64, ; 263
	i32 138, ; 264
	i32 15, ; 265
	i32 116, ; 266
	i32 286, ; 267
	i32 295, ; 268
	i32 242, ; 269
	i32 48, ; 270
	i32 70, ; 271
	i32 80, ; 272
	i32 126, ; 273
	i32 94, ; 274
	i32 121, ; 275
	i32 302, ; 276
	i32 26, ; 277
	i32 263, ; 278
	i32 97, ; 279
	i32 28, ; 280
	i32 238, ; 281
	i32 333, ; 282
	i32 311, ; 283
	i32 149, ; 284
	i32 169, ; 285
	i32 4, ; 286
	i32 98, ; 287
	i32 33, ; 288
	i32 93, ; 289
	i32 285, ; 290
	i32 196, ; 291
	i32 21, ; 292
	i32 41, ; 293
	i32 170, ; 294
	i32 327, ; 295
	i32 256, ; 296
	i32 319, ; 297
	i32 270, ; 298
	i32 301, ; 299
	i32 295, ; 300
	i32 275, ; 301
	i32 2, ; 302
	i32 180, ; 303
	i32 345, ; 304
	i32 134, ; 305
	i32 111, ; 306
	i32 197, ; 307
	i32 339, ; 308
	i32 225, ; 309
	i32 336, ; 310
	i32 58, ; 311
	i32 95, ; 312
	i32 318, ; 313
	i32 39, ; 314
	i32 236, ; 315
	i32 25, ; 316
	i32 94, ; 317
	i32 89, ; 318
	i32 99, ; 319
	i32 10, ; 320
	i32 216, ; 321
	i32 87, ; 322
	i32 182, ; 323
	i32 100, ; 324
	i32 282, ; 325
	i32 184, ; 326
	i32 302, ; 327
	i32 227, ; 328
	i32 315, ; 329
	i32 7, ; 330
	i32 267, ; 331
	i32 310, ; 332
	i32 224, ; 333
	i32 88, ; 334
	i32 186, ; 335
	i32 262, ; 336
	i32 154, ; 337
	i32 314, ; 338
	i32 176, ; 339
	i32 33, ; 340
	i32 193, ; 341
	i32 116, ; 342
	i32 223, ; 343
	i32 82, ; 344
	i32 20, ; 345
	i32 11, ; 346
	i32 162, ; 347
	i32 3, ; 348
	i32 207, ; 349
	i32 322, ; 350
	i32 213, ; 351
	i32 216, ; 352
	i32 214, ; 353
	i32 201, ; 354
	i32 177, ; 355
	i32 197, ; 356
	i32 84, ; 357
	i32 306, ; 358
	i32 64, ; 359
	i32 324, ; 360
	i32 289, ; 361
	i32 143, ; 362
	i32 271, ; 363
	i32 157, ; 364
	i32 41, ; 365
	i32 309, ; 366
	i32 117, ; 367
	i32 185, ; 368
	i32 226, ; 369
	i32 318, ; 370
	i32 278, ; 371
	i32 181, ; 372
	i32 131, ; 373
	i32 75, ; 374
	i32 66, ; 375
	i32 328, ; 376
	i32 172, ; 377
	i32 230, ; 378
	i32 143, ; 379
	i32 106, ; 380
	i32 151, ; 381
	i32 70, ; 382
	i32 156, ; 383
	i32 184, ; 384
	i32 121, ; 385
	i32 127, ; 386
	i32 323, ; 387
	i32 152, ; 388
	i32 253, ; 389
	i32 211, ; 390
	i32 141, ; 391
	i32 240, ; 392
	i32 320, ; 393
	i32 20, ; 394
	i32 14, ; 395
	i32 135, ; 396
	i32 75, ; 397
	i32 59, ; 398
	i32 243, ; 399
	i32 167, ; 400
	i32 168, ; 401
	i32 205, ; 402
	i32 15, ; 403
	i32 74, ; 404
	i32 6, ; 405
	i32 173, ; 406
	i32 23, ; 407
	i32 265, ; 408
	i32 224, ; 409
	i32 91, ; 410
	i32 321, ; 411
	i32 1, ; 412
	i32 136, ; 413
	i32 266, ; 414
	i32 288, ; 415
	i32 134, ; 416
	i32 69, ; 417
	i32 146, ; 418
	i32 192, ; 419
	i32 330, ; 420
	i32 306, ; 421
	i32 257, ; 422
	i32 200, ; 423
	i32 88, ; 424
	i32 96, ; 425
	i32 247, ; 426
	i32 252, ; 427
	i32 325, ; 428
	i32 31, ; 429
	i32 45, ; 430
	i32 261, ; 431
	i32 222, ; 432
	i32 226, ; 433
	i32 109, ; 434
	i32 158, ; 435
	i32 35, ; 436
	i32 22, ; 437
	i32 114, ; 438
	i32 57, ; 439
	i32 286, ; 440
	i32 144, ; 441
	i32 118, ; 442
	i32 120, ; 443
	i32 110, ; 444
	i32 228, ; 445
	i32 139, ; 446
	i32 234, ; 447
	i32 54, ; 448
	i32 105, ; 449
	i32 331, ; 450
	i32 206, ; 451
	i32 207, ; 452
	i32 133, ; 453
	i32 300, ; 454
	i32 291, ; 455
	i32 279, ; 456
	i32 337, ; 457
	i32 257, ; 458
	i32 210, ; 459
	i32 209, ; 460
	i32 159, ; 461
	i32 316, ; 462
	i32 244, ; 463
	i32 163, ; 464
	i32 132, ; 465
	i32 279, ; 466
	i32 161, ; 467
	i32 329, ; 468
	i32 268, ; 469
	i32 140, ; 470
	i32 291, ; 471
	i32 287, ; 472
	i32 169, ; 473
	i32 208, ; 474
	i32 222, ; 475
	i32 229, ; 476
	i32 296, ; 477
	i32 40, ; 478
	i32 255, ; 479
	i32 81, ; 480
	i32 219, ; 481
	i32 56, ; 482
	i32 37, ; 483
	i32 97, ; 484
	i32 166, ; 485
	i32 172, ; 486
	i32 193, ; 487
	i32 292, ; 488
	i32 82, ; 489
	i32 231, ; 490
	i32 223, ; 491
	i32 182, ; 492
	i32 98, ; 493
	i32 30, ; 494
	i32 159, ; 495
	i32 18, ; 496
	i32 127, ; 497
	i32 202, ; 498
	i32 119, ; 499
	i32 251, ; 500
	i32 282, ; 501
	i32 264, ; 502
	i32 284, ; 503
	i32 165, ; 504
	i32 259, ; 505
	i32 178, ; 506
	i32 346, ; 507
	i32 281, ; 508
	i32 272, ; 509
	i32 170, ; 510
	i32 16, ; 511
	i32 183, ; 512
	i32 144, ; 513
	i32 322, ; 514
	i32 125, ; 515
	i32 118, ; 516
	i32 218, ; 517
	i32 38, ; 518
	i32 198, ; 519
	i32 115, ; 520
	i32 47, ; 521
	i32 142, ; 522
	i32 117, ; 523
	i32 215, ; 524
	i32 34, ; 525
	i32 175, ; 526
	i32 181, ; 527
	i32 95, ; 528
	i32 53, ; 529
	i32 344, ; 530
	i32 273, ; 531
	i32 129, ; 532
	i32 153, ; 533
	i32 24, ; 534
	i32 161, ; 535
	i32 250, ; 536
	i32 148, ; 537
	i32 104, ; 538
	i32 309, ; 539
	i32 89, ; 540
	i32 238, ; 541
	i32 60, ; 542
	i32 142, ; 543
	i32 183, ; 544
	i32 100, ; 545
	i32 5, ; 546
	i32 13, ; 547
	i32 122, ; 548
	i32 135, ; 549
	i32 28, ; 550
	i32 317, ; 551
	i32 72, ; 552
	i32 248, ; 553
	i32 24, ; 554
	i32 236, ; 555
	i32 277, ; 556
	i32 274, ; 557
	i32 334, ; 558
	i32 137, ; 559
	i32 229, ; 560
	i32 245, ; 561
	i32 168, ; 562
	i32 278, ; 563
	i32 313, ; 564
	i32 101, ; 565
	i32 123, ; 566
	i32 249, ; 567
	i32 218, ; 568
	i32 186, ; 569
	i32 189, ; 570
	i32 163, ; 571
	i32 167, ; 572
	i32 252, ; 573
	i32 39, ; 574
	i32 204, ; 575
	i32 321, ; 576
	i32 17, ; 577
	i32 171, ; 578
	i32 334, ; 579
	i32 333, ; 580
	i32 137, ; 581
	i32 150, ; 582
	i32 241, ; 583
	i32 155, ; 584
	i32 130, ; 585
	i32 19, ; 586
	i32 65, ; 587
	i32 147, ; 588
	i32 47, ; 589
	i32 341, ; 590
	i32 227, ; 591
	i32 79, ; 592
	i32 173, ; 593
	i32 61, ; 594
	i32 219, ; 595
	i32 106, ; 596
	i32 276, ; 597
	i32 231, ; 598
	i32 49, ; 599
	i32 262, ; 600
	i32 338, ; 601
	i32 273, ; 602
	i32 14, ; 603
	i32 185, ; 604
	i32 68, ; 605
	i32 171, ; 606
	i32 237, ; 607
	i32 241, ; 608
	i32 217, ; 609
	i32 343, ; 610
	i32 78, ; 611
	i32 246, ; 612
	i32 108, ; 613
	i32 230, ; 614
	i32 188, ; 615
	i32 272, ; 616
	i32 67, ; 617
	i32 63, ; 618
	i32 344, ; 619
	i32 27, ; 620
	i32 160, ; 621
	i32 187, ; 622
	i32 239, ; 623
	i32 10, ; 624
	i32 204, ; 625
	i32 11, ; 626
	i32 221, ; 627
	i32 78, ; 628
	i32 126, ; 629
	i32 83, ; 630
	i32 190, ; 631
	i32 66, ; 632
	i32 107, ; 633
	i32 65, ; 634
	i32 128, ; 635
	i32 122, ; 636
	i32 77, ; 637
	i32 287, ; 638
	i32 277, ; 639
	i32 342, ; 640
	i32 8, ; 641
	i32 245, ; 642
	i32 2, ; 643
	i32 44, ; 644
	i32 290, ; 645
	i32 156, ; 646
	i32 307, ; 647
	i32 128, ; 648
	i32 275, ; 649
	i32 23, ; 650
	i32 133, ; 651
	i32 233, ; 652
	i32 264, ; 653
	i32 337, ; 654
	i32 319, ; 655
	i32 29, ; 656
	i32 232, ; 657
	i32 62, ; 658
	i32 206, ; 659
	i32 90, ; 660
	i32 87, ; 661
	i32 148, ; 662
	i32 199, ; 663
	i32 195, ; 664
	i32 208, ; 665
	i32 36, ; 666
	i32 86, ; 667
	i32 253, ; 668
	i32 332, ; 669
	i32 327, ; 670
	i32 189, ; 671
	i32 50, ; 672
	i32 6, ; 673
	i32 90, ; 674
	i32 339, ; 675
	i32 21, ; 676
	i32 162, ; 677
	i32 96, ; 678
	i32 50, ; 679
	i32 113, ; 680
	i32 269, ; 681
	i32 130, ; 682
	i32 76, ; 683
	i32 27, ; 684
	i32 246, ; 685
	i32 268, ; 686
	i32 7, ; 687
	i32 211, ; 688
	i32 205, ; 689
	i32 174, ; 690
	i32 110, ; 691
	i32 269, ; 692
	i32 255 ; 693
], align 4

@marshal_methods_number_of_classes = dso_local local_unnamed_addr constant i32 0, align 4

@marshal_methods_class_cache = dso_local local_unnamed_addr global [0 x %struct.MarshalMethodsManagedClass] zeroinitializer, align 4

; Names of classes in which marshal methods reside
@mm_class_names = dso_local local_unnamed_addr constant [0 x ptr] zeroinitializer, align 4

@mm_method_names = dso_local local_unnamed_addr constant [1 x %struct.MarshalMethodName] [
	%struct.MarshalMethodName {
		i64 0, ; id 0x0; name: 
		ptr @.MarshalMethodName.0_name; char* name
	} ; 0
], align 8

; get_function_pointer (uint32_t mono_image_index, uint32_t class_index, uint32_t method_token, void*& target_ptr)
@get_function_pointer = internal dso_local unnamed_addr global ptr null, align 4

; Functions

; Function attributes: "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" uwtable willreturn
define void @xamarin_app_init(ptr nocapture noundef readnone %env, ptr noundef %fn) local_unnamed_addr #0
{
	%fnIsNull = icmp eq ptr %fn, null
	br i1 %fnIsNull, label %1, label %2

1: ; preds = %0
	%putsResult = call noundef i32 @puts(ptr @.str.0)
	call void @abort()
	unreachable 

2: ; preds = %1, %0
	store ptr %fn, ptr @get_function_pointer, align 4, !tbaa !3
	ret void
}

; Strings
@.str.0 = private unnamed_addr constant [40 x i8] c"get_function_pointer MUST be specified\0A\00", align 1

;MarshalMethodName
@.MarshalMethodName.0_name = private unnamed_addr constant [1 x i8] c"\00", align 1

; External functions

; Function attributes: "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8"
declare void @abort() local_unnamed_addr #2

; Function attributes: nofree nounwind
declare noundef i32 @puts(ptr noundef) local_unnamed_addr #1
attributes #0 = { "min-legal-vector-width"="0" mustprogress "no-trapping-math"="true" nofree norecurse nosync nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+armv7-a,+d32,+dsp,+fp64,+neon,+vfp2,+vfp2sp,+vfp3,+vfp3d16,+vfp3d16sp,+vfp3sp,-aes,-fp-armv8,-fp-armv8d16,-fp-armv8d16sp,-fp-armv8sp,-fp16,-fp16fml,-fullfp16,-sha2,-thumb-mode,-vfp4,-vfp4d16,-vfp4d16sp,-vfp4sp" uwtable willreturn }
attributes #1 = { nofree nounwind }
attributes #2 = { "no-trapping-math"="true" noreturn nounwind "stack-protector-buffer-size"="8" "target-cpu"="generic" "target-features"="+armv7-a,+d32,+dsp,+fp64,+neon,+vfp2,+vfp2sp,+vfp3,+vfp3d16,+vfp3d16sp,+vfp3sp,-aes,-fp-armv8,-fp-armv8d16,-fp-armv8d16sp,-fp-armv8sp,-fp16,-fp16fml,-fullfp16,-sha2,-thumb-mode,-vfp4,-vfp4d16,-vfp4d16sp,-vfp4sp" }

; Metadata
!llvm.module.flags = !{!0, !1, !7}
!0 = !{i32 1, !"wchar_size", i32 4}
!1 = !{i32 7, !"PIC Level", i32 2}
!llvm.ident = !{!2}
!2 = !{!"Xamarin.Android remotes/origin/release/8.0.4xx @ 82d8938cf80f6d5fa6c28529ddfbdb753d805ab4"}
!3 = !{!4, !4, i64 0}
!4 = !{!"any pointer", !5, i64 0}
!5 = !{!"omnipotent char", !6, i64 0}
!6 = !{!"Simple C++ TBAA"}
!7 = !{i32 1, !"min_enum_size", i32 4}
