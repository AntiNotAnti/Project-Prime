using System.Security.Cryptography;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;

// Owned-source authoring acceptance only. No graphics/actor/ability admission.
internal static class CharacterDroneSourceOwnedProbe
{
    private const string BaselineCore = "e0a47b84083540f3465560f6c7c36ca716cdcf110e3f45718e190cba7b3b4b2f";
    private const string SourceSha = "3fc5ece5ca1badecc1e61a3762294bf81a9f3cf0ed6652eab0838aacd74fe844";
    private static readonly string[] DroneIds = ["trooper-drone-alpha", "trooper-drone-gamma"];
    private static readonly string[] TrooperIds = ["trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma"];
    // Metadata-only witnesses captured against the frozen pre-drone core and
    // the independent original Metroid3 resource inventory, respectively.
    private const string BaselineWitness = """
[{"Id":"trooper-power","Version":6,"SourceFingerprint":"c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0","RecipeHash":"f4ed78e8ad016d9045d0c1e0b3840271d8d2c054135d4235034698c61b205a6c","Components":[{"Name":"characters.json","Length":1237,"Sha256":"0887126d6b28d316f335410d600855e38b1cc96df08468171d62d9674ebbac2e"},{"Name":"trooper-power.ancs-graph.json","Length":99226,"Sha256":"303b654c5fbe3d98165ee0ef13e62a8acf9117915c687868b124c5ba6b69f0a0"},{"Name":"trooper-power.ancs.bin","Length":93069,"Sha256":"7f9ae5c119a9d9a1ddca105be582e9914e51b8c36d1435ea8ba742b7da78c5d4"},{"Name":"trooper-power.animations.bin","Length":351074,"Sha256":"a1b05e777345420456b1c2ace0c961a07bb2c54992536d30ef656e78593257b8"},{"Name":"trooper-power.cannon-source.json","Length":2169,"Sha256":"2b128462891c9aa85ebae92b68306345386c7dcaeabdee043ff522de69a4ecc5"},{"Name":"trooper-power.cinf.bin","Length":1760,"Sha256":"937aa40d2074d819a6d4d89946eb7a059f44db73f6f2b54854de7b914b7ddbf9"},{"Name":"trooper-power.materials.bin","Length":59172,"Sha256":"50f5acc05dc966847c96f05e9bafa045777510e77d6e3a3f224227020a9c553a"},{"Name":"trooper-power.motions.json","Length":254067,"Sha256":"5dfad06f86a4e844751d73dfcd459e373c9c3f6714c93853990986156c9832a5"},{"Name":"trooper-power.obj","Length":357588,"Sha256":"bad4506ca9a876b56dc61946e4cf1a352b7f30aa3418aa2665971e0ab584b123"},{"Name":"trooper-power.skeleton.json","Length":2987,"Sha256":"c2a04406cb4dda3a472ed8d18ffa91e12fe2cb56fd31b434fa47bfc75371e927"},{"Name":"trooper-power.source.glb","Length":373960,"Sha256":"54f298805409fa12f34c34203cf7db2f5085f5b5ec4410ac04b3d329da71f3e8"},{"Name":"trooper-power.source.json","Length":1416,"Sha256":"512da9abe64956aa8d7765611cef9dc596eea96c5403353013cfad8bbe4717c3"},{"Name":"trooper-power.weights.json","Length":6207,"Sha256":"f4c881a04a51732bf9965a09498316d3ed0d7eadf886152e1906d4964ba5cfc3"}]},{"Id":"trooper-ice","Version":6,"SourceFingerprint":"c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0","RecipeHash":"9d274fdff8fc6082972fb9c8b906a11e2865f219baf92e077ad2279f383a8bea","Components":[{"Name":"characters.json","Length":1217,"Sha256":"269abc7edc36fed7e54d9f8fd91db6205c867ef5c9841933cec84f37fc5766ba"},{"Name":"trooper-ice.ancs-graph.json","Length":99219,"Sha256":"fe009241436842decaeb9c578cc23bab5f8c4f5cbfa7af924c14384d976cf3ad"},{"Name":"trooper-ice.ancs.bin","Length":93069,"Sha256":"7f9ae5c119a9d9a1ddca105be582e9914e51b8c36d1435ea8ba742b7da78c5d4"},{"Name":"trooper-ice.animations.bin","Length":351069,"Sha256":"63ef357c9c0c945bb3ff97bf23b891cc028cd3de27b45dd9148d574cf927ce44"},{"Name":"trooper-ice.cannon-source.json","Length":2273,"Sha256":"2074a896f47f0bd72402076afa1347287958bfef5682a225a287e4a83afe9363"},{"Name":"trooper-ice.cinf.bin","Length":1760,"Sha256":"937aa40d2074d819a6d4d89946eb7a059f44db73f6f2b54854de7b914b7ddbf9"},{"Name":"trooper-ice.materials.bin","Length":42788,"Sha256":"16f6f537954145940030874afb6d8dae3187de08e4f5997568085c3ad96701f0"},{"Name":"trooper-ice.motions.json","Length":254063,"Sha256":"41c119674d4c701e00cef2f941a9bd2f1adadfc83bed4c82014f667c57ced5eb"},{"Name":"trooper-ice.obj","Length":357502,"Sha256":"fce7d8c64e3604c4c0e9a3d00d66b7d5f7cdd1e86f3c241090ed09c8d1ead05b"},{"Name":"trooper-ice.skeleton.json","Length":2982,"Sha256":"5f93f3df2d0be1fb3a75a65541ed39c73540e8ba7b76db10b979810f5a72775e"},{"Name":"trooper-ice.source.glb","Length":373960,"Sha256":"c3f7c8f9c77824937522db1199970a5f9d0be94e8dbfdbe35a66b7c5adfac307"},{"Name":"trooper-ice.source.json","Length":1411,"Sha256":"0aaa87d7b142f8ce16e71d9bf7e5741c254720cf758e6818a651055c66078364"},{"Name":"trooper-ice.weights.json","Length":6207,"Sha256":"f4c881a04a51732bf9965a09498316d3ed0d7eadf886152e1906d4964ba5cfc3"}]},{"Id":"trooper-wave","Version":6,"SourceFingerprint":"c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0","RecipeHash":"67fcc774cc8034254fb4f7d98dac04e97c02c642cae14adefc4e0e6b3c3381b3","Components":[{"Name":"characters.json","Length":1227,"Sha256":"35a9e78ea7423aaf0a69d38d43931eb15b087624d043bebe0ae02d6eb50c08af"},{"Name":"trooper-wave.ancs-graph.json","Length":99222,"Sha256":"76e049702e73ca0907d4ff8e80331b7bbcd77070f39b23f0be7a85af2c8f7355"},{"Name":"trooper-wave.ancs.bin","Length":93069,"Sha256":"7f9ae5c119a9d9a1ddca105be582e9914e51b8c36d1435ea8ba742b7da78c5d4"},{"Name":"trooper-wave.animations.bin","Length":351071,"Sha256":"6b95ba35dce96cd3fbfd6de5709b51bef89bbea1777ae3725556d9f484f648d7"},{"Name":"trooper-wave.cannon-source.json","Length":2162,"Sha256":"1c1cb397a741ebab61d1a36d785c194b4bad7f764c9d6684d86ec9f50e42c79b"},{"Name":"trooper-wave.cinf.bin","Length":1760,"Sha256":"937aa40d2074d819a6d4d89946eb7a059f44db73f6f2b54854de7b914b7ddbf9"},{"Name":"trooper-wave.materials.bin","Length":42788,"Sha256":"d84ae5a8ecfe71703020dfc0348fcac23713726e5af03c9fc26942654c1d2c6b"},{"Name":"trooper-wave.motions.json","Length":254065,"Sha256":"07be6a8a0251f22c89c06f6a473cf3b0043f8ec5a03ae3f372b34bb9dc06539b"},{"Name":"trooper-wave.obj","Length":357493,"Sha256":"35abe854876a78014c96312dd62f6e426f5c4bb5ce1ada442c92bcbb60f162d1"},{"Name":"trooper-wave.skeleton.json","Length":2984,"Sha256":"f0ec263b8faa826db85be8e85f0952a2f687491b4a8b546ce6b0fae18d6e6d59"},{"Name":"trooper-wave.source.glb","Length":373960,"Sha256":"6a307e91a5bfdd82a0f1ae904b3179917fc7f3da70e2c3546ae2feffec32653b"},{"Name":"trooper-wave.source.json","Length":1413,"Sha256":"25ed588b1413e63b6a6631a9c2797d41b35173f8de70d0994ddbdc5dda046c81"},{"Name":"trooper-wave.weights.json","Length":6207,"Sha256":"f4c881a04a51732bf9965a09498316d3ed0d7eadf886152e1906d4964ba5cfc3"}]},{"Id":"trooper-plasma","Version":6,"SourceFingerprint":"c55bf0ecb6cac32fa90d43cb0ad9e4a917d43ed0","RecipeHash":"179cc9d2cd33c7c3e6a3471a7d6b58e5441dac236de2686503003cf532085223","Components":[{"Name":"characters.json","Length":1247,"Sha256":"0b5cab55796cf50dd934cdd0a7d1fd43062a652ea86eddbe80b60ffab9026d74"},{"Name":"trooper-plasma.ancs-graph.json","Length":99228,"Sha256":"0a759359ccdf81f510962282f0458f3b341331743678952b649f08c07d84a9f5"},{"Name":"trooper-plasma.ancs.bin","Length":93069,"Sha256":"7f9ae5c119a9d9a1ddca105be582e9914e51b8c36d1435ea8ba742b7da78c5d4"},{"Name":"trooper-plasma.animations.bin","Length":351075,"Sha256":"3ee7cd22d0c64374c28c08ba321c4b9d62d8b3ee653c04cd922836e089aeb7b2"},{"Name":"trooper-plasma.cannon-source.json","Length":2294,"Sha256":"1f8a1dc3eaf7d5a43cdc3d7e2fde28cdff63b95040f066fad57e1eb2d86584c1"},{"Name":"trooper-plasma.cinf.bin","Length":1760,"Sha256":"937aa40d2074d819a6d4d89946eb7a059f44db73f6f2b54854de7b914b7ddbf9"},{"Name":"trooper-plasma.materials.bin","Length":42788,"Sha256":"0eb01263a7dad10519af2c70e3158fd623b3a56bf18251c625e7761e651a5933"},{"Name":"trooper-plasma.motions.json","Length":254069,"Sha256":"2a34e82342cee99d98771e0dd507ea759dd9e1fa51d2a1dc16f99438c5831189"},{"Name":"trooper-plasma.obj","Length":357490,"Sha256":"ed3696d135b3cbe1f11d96060f4ee7512995a7984f9f49e7b61cf59340708b5a"},{"Name":"trooper-plasma.skeleton.json","Length":2988,"Sha256":"14f356be0d1dbeee12e7a421b5213a633a4b84cc4e50282cc95b8e7aa2ed57cc"},{"Name":"trooper-plasma.source.glb","Length":373960,"Sha256":"7877c9f7b7a27dc8c804c04ea5dc03e364504e71f3c7e744d58e95e688e4e96f"},{"Name":"trooper-plasma.source.json","Length":1417,"Sha256":"29806c36a3283672d6930d7a84e4a7ab5a48b5d95c66ef53846b2b55c2ccd858"},{"Name":"trooper-plasma.weights.json","Length":6207,"Sha256":"f4c881a04a51732bf9965a09498316d3ed0d7eadf886152e1906d4964ba5cfc3"}]}]
""";
    private const string RawWitness = """
[{"Name":"AlphaDrone","Ancs":"5e058399","Cmdl":"ef920604","Cskr":"84bda9d2","Cinf":"c3af0235","AncsSha256":"c047384a7de665b548904f50ecb7144354bf765fb986243bdfab1692ba453795","CmdlSha256":"73e6ca175045b0968db2b088b03d2d68ffceb694014f0d65ad12b15ba01d63f8","CskrSha256":"23f6bccf502dfbbb976de76d377653048541cf851759163747d2908af53a4814","CinfSha256":"c15a40b472878958c71a84fa7912ca7535aef9c9f5e3692b7738f1707ca00c9c","Geometry":{"Positions":"9469c2dbce1ebd9f3ac4bf767a8dd55ed8b882b6c6e3f53b3c67a068a6975293","Normals":"c2852adebb194ada994e50e7c92104afacb8ba7b80aac19b1daff7bde35533b6","Uvs":"5f08529e0c45681cc601bdf10a7cceb268c40ed6ad6ba0433d2f6cf812e97dc0","CornerAttributes":"0a7054f6086c71cd766d85210e4461d294dda91ad7fea5d51140b80e233e9af7","TriangleMaterials":"de964d3283f70e2cb5a591212bd5ec2b6aeabdf76134de5c5aa1a9674dfda07b"},"Clips":[{"Ordinal":13,"Name":"B_ready_drone","ResourceId":"b45b6445","Sha256":"d714b3f01328ad897a82ed67de2f3e20e0922283bd9e453c269939684cc46cb0","KeyCount":40,"Duration":1.2999999523162842},{"Ordinal":4,"Name":"B_floatloopf_drone","ResourceId":"1f21510a","Sha256":"857458dfcc824d8d57a19762eb5f95e5f9eb26fcd2725a43c19e5013510012c7","KeyCount":40,"Duration":1.2999999523162842},{"Ordinal":3,"Name":"B_floatloopb_drone","ResourceId":"aff82288","Sha256":"fa217ba84b3b7d0d871fbdd9b3c2798951ed3d7e01ef430e08ca855571a046e1","KeyCount":40,"Duration":1.2999999523162842},{"Ordinal":2,"Name":"B_floatleft_drone","ResourceId":"fa0107c9","Sha256":"898e9a5304cf9f38fbca896bc8a22b5acebadb8e3c8ec4ccc2a8baf783483fa7","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":5,"Name":"B_floatright_drone","ResourceId":"e87fa8e3","Sha256":"891ed408224bee08beeae347dc527c68028bfe694add7a861b5fcf6ae33680e3","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":6,"Name":"B_floatup_drone","ResourceId":"ab7bdfeb","Sha256":"b1c6873384fe362de16cbd8c8df1e90b7c4612c8f613852729d713d536c8bbc8","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":1,"Name":"B_floatdown_drone","ResourceId":"28e34205","Sha256":"bfa409602fa9b4858f2aded40ca7b81257b209d117b12a99305e7c10f9203c86","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":75,"Name":"A_intoshootloop_drone","ResourceId":"dbd60f49","Sha256":"eb724be315c52be55b7d7213cbfe386f886c497fd86ad18bfce8912bc7643cce","KeyCount":17,"Duration":0.5333330035209656},{"Ordinal":73,"Name":"A_shootloop_drone","ResourceId":"90d23d02","Sha256":"1ed47ddcdb8105a7f914c2b5de87658c32b9bf4daf8aec34dee19dbcbcd2991d","KeyCount":5,"Duration":0.1333329975605011},{"Ordinal":74,"Name":"A_outofshootloop_drone","ResourceId":"83618e64","Sha256":"707500e064576b053e04d18c63ee5007ffea8810dc979a105df9ec5dcdebe35a","KeyCount":13,"Duration":0.4000000059604645},{"Ordinal":18,"Name":"R_hitf_drone","ResourceId":"944ca609","Sha256":"eb5947bc1a81463b51b0ea2f0bd8039eec594ed0f2afaaf8e2ad7d00ef7f0ec1","KeyCount":37,"Duration":1.2000000476837158},{"Ordinal":29,"Name":"A_cuttinglaser_drone","ResourceId":"6d6b6f9f","Sha256":"b9fd4217ace08e9431ead637825f8125e28978c212c465395e0c53edb6d8675c","KeyCount":68,"Duration":2.233333110809326}],"Textures":[{"Id":"46824e6c","Bytes":2732,"Sha256":"b1c7955585faa84506ed893b7d85e7cd2eef5b4a238f943868971d2a27c587c0","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"8d62c91cdc70f11c2aafe3a7d6c24615eed51c4a3038b5e56c0abce46c913eb7"},{"Id":"54b2e3cf","Bytes":10924,"Sha256":"9ff24fe3fbe4fd76548c175872faaf3e7a40eecd58862983c004dba6a22f387e","Format":10,"Width":128,"Height":128,"MipCount":5,"DecodedTopRgbaSha256":"75a8d43207d3d9aa9f4eb10db9e09e7cc717ee2481edf351b9fee995f282243f"},{"Id":"852cefed","Bytes":2732,"Sha256":"ed694d4e9bb6473a3353b01b58026ed59ac4ea12ddd5f43f18a09603929cd16b","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"7a02f68e54f1174c4bae48adcde64fc6ca6585cfb1633b0ac8d258323520c657"},{"Id":"a8fcff83","Bytes":2732,"Sha256":"db96767081e9dd89453b255278663808fdcbb1e01f749a87f232e9016f75d1d8","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"83ecfa2d8743685cc694e9b036fd95b017d9e88c4657cb2413f641b0ff067469"},{"Id":"bbee381a","Bytes":2732,"Sha256":"25f6faa2c0384964fd92bb0d9aaa84afb115ceb7cb00b9071ee3dcf4cc3faf6a","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"10170c90056c34364cc9130841ad42e7a19e34644ed9d69a2a8cad54efe43b5b"},{"Id":"ef08844d","Bytes":10924,"Sha256":"12d1bace43b18307bc7a0666c8aa1c64766765f27b878486155ab621925b9416","Format":10,"Width":128,"Height":128,"MipCount":5,"DecodedTopRgbaSha256":"7faf8022c5b4ed095affe396f5dcb1f28f8a9f71f77cecf356932ab9d75562f9"},{"Id":"ff32bc91","Bytes":2732,"Sha256":"f82a86031ea8f9f1cd71fb7cd783d76ca1d4aa082274f9d702168dab867deec0","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"27f8a62102b52b739a7572ff06c3a7cd2ca2e1e7ccd6d6148033402c282282d6"}],"Materials":[{"Ordinal":0,"OriginalMaterialSha256":"021053fbdffa1819ef5226a2caff9b5a4a23ddf6cafb8726ef12f1474b13df05"},{"Ordinal":1,"OriginalMaterialSha256":"a64a6f50e3094db401404bcadd50d3ea1671ae5f2cdc998e6c49fb6c2cd9f9ba"}]},{"Name":"Gamma_Drone","Ancs":"5e058399","Cmdl":"0b6586cb","Cskr":"604a291d","Cinf":"c3af0235","AncsSha256":"c047384a7de665b548904f50ecb7144354bf765fb986243bdfab1692ba453795","CmdlSha256":"0650cc1991e326d49a9d5cd73261b79836be6e0a4c4023081722d49f09a46257","CskrSha256":"23f6bccf502dfbbb976de76d377653048541cf851759163747d2908af53a4814","CinfSha256":"c15a40b472878958c71a84fa7912ca7535aef9c9f5e3692b7738f1707ca00c9c","Geometry":{"Positions":"3b46eccb61904edaef90d8c3639df8e7cc36862877621a31060fb7ee0a472b7f","Normals":"da4ed4739c9eecf6619f9d732fb852f225c9e3228eaf943a1a88ae6d5079c176","Uvs":"6b7ee6bcf2ea098ab9ec3f55be3ec3b4200b3d33b955e5744dd2fea1c6f2b5cb","CornerAttributes":"73e1d9d91e65f23cd151adaa9ea6f449f3f6b8b3ab9315ffa62f47c4429ad4fe","TriangleMaterials":"fbc81f859a9691bafd0bc5721fad28a7d15662cffd7683c1a50f53f91dca999e"},"Clips":[{"Ordinal":13,"Name":"B_ready_drone","ResourceId":"b45b6445","Sha256":"d714b3f01328ad897a82ed67de2f3e20e0922283bd9e453c269939684cc46cb0","KeyCount":40,"Duration":1.2999999523162842},{"Ordinal":4,"Name":"B_floatloopf_drone","ResourceId":"1f21510a","Sha256":"857458dfcc824d8d57a19762eb5f95e5f9eb26fcd2725a43c19e5013510012c7","KeyCount":40,"Duration":1.2999999523162842},{"Ordinal":3,"Name":"B_floatloopb_drone","ResourceId":"aff82288","Sha256":"fa217ba84b3b7d0d871fbdd9b3c2798951ed3d7e01ef430e08ca855571a046e1","KeyCount":40,"Duration":1.2999999523162842},{"Ordinal":2,"Name":"B_floatleft_drone","ResourceId":"fa0107c9","Sha256":"898e9a5304cf9f38fbca896bc8a22b5acebadb8e3c8ec4ccc2a8baf783483fa7","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":5,"Name":"B_floatright_drone","ResourceId":"e87fa8e3","Sha256":"891ed408224bee08beeae347dc527c68028bfe694add7a861b5fcf6ae33680e3","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":6,"Name":"B_floatup_drone","ResourceId":"ab7bdfeb","Sha256":"b1c6873384fe362de16cbd8c8df1e90b7c4612c8f613852729d713d536c8bbc8","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":1,"Name":"B_floatdown_drone","ResourceId":"28e34205","Sha256":"bfa409602fa9b4858f2aded40ca7b81257b209d117b12a99305e7c10f9203c86","KeyCount":27,"Duration":0.8666660189628601},{"Ordinal":75,"Name":"A_intoshootloop_drone","ResourceId":"dbd60f49","Sha256":"eb724be315c52be55b7d7213cbfe386f886c497fd86ad18bfce8912bc7643cce","KeyCount":17,"Duration":0.5333330035209656},{"Ordinal":73,"Name":"A_shootloop_drone","ResourceId":"90d23d02","Sha256":"1ed47ddcdb8105a7f914c2b5de87658c32b9bf4daf8aec34dee19dbcbcd2991d","KeyCount":5,"Duration":0.1333329975605011},{"Ordinal":74,"Name":"A_outofshootloop_drone","ResourceId":"83618e64","Sha256":"707500e064576b053e04d18c63ee5007ffea8810dc979a105df9ec5dcdebe35a","KeyCount":13,"Duration":0.4000000059604645},{"Ordinal":18,"Name":"R_hitf_drone","ResourceId":"944ca609","Sha256":"eb5947bc1a81463b51b0ea2f0bd8039eec594ed0f2afaaf8e2ad7d00ef7f0ec1","KeyCount":37,"Duration":1.2000000476837158},{"Ordinal":29,"Name":"A_cuttinglaser_drone","ResourceId":"6d6b6f9f","Sha256":"b9fd4217ace08e9431ead637825f8125e28978c212c465395e0c53edb6d8675c","KeyCount":68,"Duration":2.233333110809326}],"Textures":[{"Id":"a8fcff83","Bytes":2732,"Sha256":"db96767081e9dd89453b255278663808fdcbb1e01f749a87f232e9016f75d1d8","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"83ecfa2d8743685cc694e9b036fd95b017d9e88c4657cb2413f641b0ff067469"},{"Id":"ba902127","Bytes":2732,"Sha256":"bac9290a46bdfc6575dec7eb1725cca675bf564baef8e92c0b583af74be26227","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"0fb3bf36affd6ef87d3ea27e0ad03ae311fc63b2e5e983f0d03c84f2977008c1"},{"Id":"e9053721","Bytes":10924,"Sha256":"67cc938d9048764b0957b4264f1b6a70e5023d07637b2e3f2f1e36336f7234ed","Format":10,"Width":128,"Height":128,"MipCount":5,"DecodedTopRgbaSha256":"6baef2026a63e2361338f50308e8147110505869f748882e65f5bb71aac1ca7e"},{"Id":"eaabe11f","Bytes":10924,"Sha256":"a3f02fbf79da3c137132c4d3f97ac1d901d25d532cfe6d006af27b578d3f3248","Format":10,"Width":128,"Height":128,"MipCount":5,"DecodedTopRgbaSha256":"146864b47e889e556ce41f780fb6e40d60e31a0d75d9574344944480e0ab7902"},{"Id":"ff32bc91","Bytes":2732,"Sha256":"f82a86031ea8f9f1cd71fb7cd783d76ca1d4aa082274f9d702168dab867deec0","Format":10,"Width":64,"Height":64,"MipCount":4,"DecodedTopRgbaSha256":"27f8a62102b52b739a7572ff06c3a7cd2ca2e1e7ccd6d6148033402c282282d6"}],"Materials":[{"Ordinal":0,"OriginalMaterialSha256":"aa6baa19540166b883700cd2c879254c01432d8681fa397f59301265e2dea4cb"},{"Ordinal":1,"OriginalMaterialSha256":"a5b2009fcfcb9183d8cc52ea336355c091970f3d8483bcd9ecd0764fb1366aca"}]}]
""";

    internal static int Run(string source, bool captureBaseline = false)
    {
        string core = typeof(RetroCharacterPreviewExporter).Assembly.Location;
        string tool = typeof(CharacterDroneSourceOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool);
        string temporary = Path.Combine(Path.GetTempPath(), "prime-drone-source-" + Guid.NewGuid().ToString("N"));
        string? before = null, after = null, failed = null, error = null;
        int checks = 0; bool sourceIo = false, cleaned = false;
        uint rng1 = Rng.Rng1, rng2 = Rng.Rng2; var bundles = new List<object>();
        void Check(bool condition, string label)
        {
            if (!condition) { failed = label; throw new InvalidDataException(label); }
            checks++;
        }
        try
        {
            if (!captureBaseline)
            {
                // This deliberately precedes any source path validation or I/O:
                // the old-core tests-first result must not touch owned media.
                Check(DroneIds.All(id => RetroCharacterPreviewExporter.Sources.Count(s => s.Id == id) == 1),
                    "independent Alpha/Gamma source-drone authoring IDs exist before owned-source I/O");
                Check(RetroCharacterPreviewExporter.Sources.Where(s => DroneIds.Contains(s.Id)).All(s =>
                    s.Game == ExternalGameKind.MetroidPrime && s.Ancs == 0x5e058399),
                    "new drone sources bind their own Prime ANCS rather than Trooper body ANCS");
                CheckSelections(Check);
                sourceIo = true; before = HashFile(source);
                Check(before == SourceSha, "source is the independently pinned owned Prime USA Revision2 RVZ");
                bundles.AddRange(VerifyAuthoring(source, temporary, Check));
            }
            else
            {
            Check(coreBefore == BaselineCore, "old Trooper export witness uses exact frozen qualified e0a core");
            sourceIo = true; before = HashFile(source);
            Check(before == SourceSha, "baseline source is the previously verified owned Prime USA Revision2 RVZ");
            string imports = Path.Combine(temporary, "imports");
            var files = RetroCharacterPreviewExporter.ExportFromOwnedGame(source, imports, selectedIds: TrooperIds);
            Check(files.Count == 4, "baseline exports exactly four original Trooper appearances");
            string fingerprint = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(
                "AudioGrp.pak:157e0841155cc7b699176dd7afc746c7048dbc28\nMetroid1.pak:2147a5ea1ff654d6bbce749d5f7c1170f98a601e")));
            foreach (string id in TrooperIds)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(id, ExternalSourceEdition.PrimeUsaRevision2);
                var state = RetroCharacterPreviewExporter.InspectBundle(imports, id);
                Check(spec.Version == 6 && spec.SourceFingerprint == fingerprint && spec.RequiredComponents.Length == 13
                    && state.State == ImportBundleState.Ready && state.Receipt != null && state.Files.Count == 13,
                    "baseline retains original Rev2 Trooper v6 identity and all13 committed components: " + id);
                var components = state.Files.OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path =>
                {
                    Check(Path.GetFullPath(path).StartsWith(Path.GetFullPath(imports) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                        "baseline component stays inside disposable import root");
                    var component = state.Receipt!.Components.Single(c => c.Id == Path.GetFileName(path));
                    string sha = HashFile(path); long bytes = new FileInfo(path).Length;
                    Check(component.Sha256 == sha && component.Length == bytes, "baseline receipt matches complete component bytes");
                    return new { Name = Path.GetFileName(path), Length = bytes, Sha256 = sha };
                }).ToArray();
                bundles.Add(new { Id = id, spec.Version, spec.SourceFingerprint, spec.RecipeHash, Components = components });
            }
            Check(bundles.Count == 4 && Rng.Rng1 == rng1 && Rng.Rng2 == rng2, "baseline captures52 original component hashes without native RNG mutation");
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            Console.Error.WriteLine("OWNED DRONE SOURCE failure: " + ex);
        }
        finally
        {
            try
            {
                string target = Path.GetFullPath(temporary);
                if (!target.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(target).StartsWith("prime-drone-source-", StringComparison.Ordinal))
                    throw new InvalidDataException("Drone fixture cleanup escaped its own temporary root.");
                if (Directory.Exists(target)) Directory.Delete(target, true);
                cleaned = !Directory.Exists(target);
                if (sourceIo) after = HashFile(source);
                Check(cleaned && (!sourceIo || before != null && before == after), "disposable imports deleted and any opened source unchanged");
                Check(HashFile(core) == coreBefore && HashFile(tool) == toolBefore && Rng.Rng1 == rng1 && Rng.Rng2 == rng2,
                    "core/tool and native RNG unchanged by authoring fixture");
            }
            catch (Exception ex) { error ??= ex.GetType().Name; }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Pass = error == null, Checks = checks, FailedCheck = failed, ErrorType = error,
                BaselineOnly = captureBaseline, CoreSha256 = coreBefore, ToolSha256 = toolBefore,
                SourceIoAttempted = sourceIo, SourceSha256Before = before, SourceSha256After = after,
                TemporaryDerivedDataDeleted = cleaned, Bundles = bundles,
                Scope = captureBaseline ? "four original Trooper v6 export metadata witnesses,52 component hashes only"
                    : "independent Alpha/Gamma v1 authoring: own33-joint full1390 triangles/raw materials/ANCS/strict banks,52 old Trooper components preserved, per-source transaction refusals; no runtime or GPU capability claim",
                Unqualified = new[] { "GPU", "actors", "drone ability or gameplay", "native UI", "replay/network admission" }
            }));
        }
        return error == null ? 0 : 1;
    }

    private static IEnumerable<object> VerifyAuthoring(string source,string temporary,Action<bool,string> check)
    {
        var result=new List<object>();int refusals=0;
        void Reject(Action action,string why)
        {
            try{action();}catch(Exception ex)when(ex is InvalidDataException or NotSupportedException or ArgumentException or IOException or OperationCanceledException){refusals++;return;}
            throw new InvalidDataException("Expected authoring refusal: "+why);
        }
        string imports=Path.Combine(temporary,"imports"),individual=Path.Combine(temporary,"individual");
        const string audio="157e0841155cc7b699176dd7afc746c7048dbc28",metroid3="30953a16e8820f7d1a5425084e04541a0439617a";
        string fingerprint=Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes("AudioGrp.pak:"+audio+"\nMetroid3.pak:"+metroid3)));
        var global=ExternalSourceRequirements.For(ExternalGameKind.MetroidPrime,ExternalSourceEdition.PrimeUsaRevision2);
        check(global.Count==2&&global.Single(r=>r.Path=="Metroid1.pak").ExpectedSha1=="2147a5ea1ff654d6bbce749d5f7c1170f98a601e"&&global.Single(r=>r.Path=="AudioGrp.pak").ExpectedSha1==audio,
            "global PrimeRev2 archive authority retains only its original two exact pins");
        using var disc=GameCubeDiscReader.Open(source);
        check(disc.GameId=="GM8E01"&&disc.Revision==2&&disc.DiscNumber==0&&ExternalSourceRequirements.EditionFor(disc)==ExternalSourceEdition.PrimeUsaRevision2,"exact owned drone source edition before resource reads");
        check(ExternalSourceRequirements.Verify(disc,new("Metroid3.pak",metroid3)).Status==ExternalArchiveStatus.Verified
            &&ExternalSourceRequirements.Verify(disc,new("AudioGrp.pak",audio)).Status==ExternalArchiveStatus.Verified,"independent drone Metroid3+AudioGrp archive pins reproduce");
        using var pak=new RetroPakReader(disc.OpenRead("Metroid3.pak"),leaveOpen:false);byte[] Resource(uint id)=>pak.ReadResource(id);
        RetroCharacterPreviewExporter.ExportFromOwnedGame(source,imports,selectedIds:TrooperIds);
        CheckOldTroopers(imports,check);
        var published=RetroCharacterPreviewExporter.ExportFromOwnedGame(source,imports,selectedIds:DroneIds);
        check(published.Count==2&&published.Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(DroneIds.Select(id=>id+".drone.obj").Order(StringComparer.Ordinal)),"group selection returns both exact independent drone OBJ stems");
        using var witnesses=JsonDocument.Parse(RawWitness);
        foreach(string id in DroneIds)
        {
            bool alpha=id.EndsWith("alpha",StringComparison.Ordinal);string stem=id+".drone",name=alpha?"AlphaDrone":"Gamma_Drone";
            var pin=witnesses.RootElement[alpha?0:1];uint Number(string field)=>Convert.ToUInt32(pin.GetProperty(field).GetString(),16);
            uint cmdlId=Number("Cmdl"),cskrId=Number("Cskr");
            var required=RetroCharacterPreviewExporter.RequiredSourceArchives(id,ExternalSourceEdition.PrimeUsaRevision2);
            check(required.Count==2&&required.Single(r=>r.Path=="Metroid3.pak").ExpectedSha1==metroid3&&required.Single(r=>r.Path=="AudioGrp.pak").ExpectedSha1==audio,"drone per-source archive authority is closed and contains no Metroid1 alias");
            Reject(()=>RetroCharacterPreviewExporter.BundleSpec(id,ExternalSourceEdition.OriginalRecipe),"wrong original source edition");
            Reject(()=>RetroCharacterPreviewExporter.RequiredSourceArchives(id,ExternalSourceEdition.OriginalRecipe),"wrong original archive authority");
            var spec=RetroCharacterPreviewExporter.BundleSpec(id,ExternalSourceEdition.PrimeUsaRevision2);
            string[] suffixes=[".obj",".skeleton.json",".weights.json",".motions.json",".cinf.bin",".animations.bin",".source.glb",".source.json",".ancs-graph.json",".ancs.bin",".materials.bin"];
            var names=suffixes.Select(s=>stem+s).Append("characters.json").ToHashSet(StringComparer.Ordinal);
            check(spec.Version==1&&spec.SourceFingerprint==fingerprint&&RetroCharacterPreviewExporter.BundleSpec(id).RecipeHash==spec.RecipeHash
                &&spec.RequiredComponents.Length==12&&names.SetEquals(spec.RequiredComponents),"independent drone v1 exact12-component recipe binds own source archives");
            var state=RetroCharacterPreviewExporter.InspectBundle(imports,id);
            check(state.State==ImportBundleState.Ready&&state.Files.Count==12&&state.Receipt is{Version:1}&&state.Receipt.RecipeHash==spec.RecipeHash,"complete drone transaction is ready with exact own recipe");
            var part=RetroCharacterPreviewExporter.InspectParts(id,state).Single();
            check(part.Id=="drone"&&part.Version==1&&part.State==ImportBundleState.Ready&&part.Components.Count==12&&part.Components.All(c=>c.State==ImportBundleState.Ready)
                &&RetroCharacterPreviewExporter.PartsFor(id).Single().Id=="drone"&&RetroCharacterPreviewExporter.PartStem(id,"drone")==stem,"single drone v1 part has no body/ball/cannon alias");
            string folder=Path.GetDirectoryName(state.Files.Single(f=>Path.GetFileName(f)=="characters.json"))!;
            check(state.Files.All(f=>Path.GetDirectoryName(f)==folder)&&Path.GetFileName(folder)==state.Receipt!.Generation,"all12 components share one immutable captured generation");
            byte[] ancs=Resource(0x5e058399),cmdl=Resource(cmdlId),cskr=Resource(cskrId),cinf=Resource(0xc3af0235);
            foreach(var tuple in new[]{("Ancs",ancs),("Cmdl",cmdl),("Cskr",cskr),("Cinf",cinf)})check(Sha(tuple.Item2)==pin.GetProperty(tuple.Item1+"Sha256").GetString(),"original drone resource reproduces independent literal raw SHA: "+tuple.Item1);
            check(File.ReadAllBytes(Path.Combine(folder,stem+".ancs.bin")).SequenceEqual(ancs)&&File.ReadAllBytes(Path.Combine(folder,stem+".cinf.bin")).SequenceEqual(cinf),"original own ANCS/CINF bytes survive without rewriting");
            var refs=RetroCharacterSource.FindCharacter(ancs,name);check(refs.Cmdl==cmdlId&&refs.Cskr==cskrId&&refs.Cinf==0xc3af0235,"own ANCS selects exact original source model/skin/rig");
            var model=Cmdl.Load(Resource,cmdlId,cskrId);var rig=RetroCharacterSource.ReadSkeleton(cinf);
            check(model.Version==2&&model.PositionCount==1663&&model.TriangleCount==1390&&model.Materials.Length==2&&model.TriangleMaterial.Count(m=>m==0)==970&&model.TriangleMaterial.Count(m=>m==1)==420
                &&rig.Bones.Count==33&&rig.Bones[3].Parent==2&&!rig.Bones.ContainsKey(2)&&model.Skin!.All(g=>g.Count(i=>i.Weight>0)==1&&g.Where(i=>i.Weight>0).All(i=>rig.Bones.ContainsKey(i.Bone))),"whole own model preserves1390 triangles/two970+420 material batches/full33 source bones and every positive influence");
            var geometryPin=pin.GetProperty("Geometry");
            check(Sha(JsonSerializer.SerializeToUtf8Bytes(model.Positions))==geometryPin.GetProperty("Positions").GetString()
                &&Sha(JsonSerializer.SerializeToUtf8Bytes(model.Normals))==geometryPin.GetProperty("Normals").GetString()
                &&Sha(JsonSerializer.SerializeToUtf8Bytes(model.Uv0))==geometryPin.GetProperty("Uvs").GetString()
                &&Sha(JsonSerializer.SerializeToUtf8Bytes(model.Triangles.Select(c=>new[]{c.P,c.N,c.T0,c.T1,c.T2,c.T3})))==geometryPin.GetProperty("CornerAttributes").GetString()
                &&Sha(JsonSerializer.SerializeToUtf8Bytes(model.TriangleMaterial))==geometryPin.GetProperty("TriangleMaterials").GetString(),"all raw geometry attributes agree with preimplementation independent inventory");
            byte[] glb=File.ReadAllBytes(Path.Combine(folder,stem+".source.glb"));object geometry=VerifyGlb(glb,model,rig,check);
            byte[] descriptor=File.ReadAllBytes(Path.Combine(folder,stem+".ancs-graph.json")),bankBytes=File.ReadAllBytes(Path.Combine(folder,stem+".animations.bin"));
            var graph=RetroAnimationGraphProof.Verify(descriptor,ancs);
            check(graph.SourceCharacterId==id&&graph.SourcePart=="drone"&&graph.SourceGameId=="GM8E01"&&graph.SourceAncsId==0x5e058399&&graph.Cmdl==cmdlId&&graph.Cskr==cskrId&&graph.Cinf==0xc3af0235,"graph binds own new ID, drone part and exact resources");
            var capture=new RetroAnimationGraphProof.Capture(ancs,descriptor,graph);var bank=RetroCharacterAnimationArchive.Read(bankBytes,capture,cinf,false);
            check(bank.Roots.Count==100&&bank.Roots.Count(r=>r.StrictPlay)==88&&bank.Roots.Count(r=>r.CharacterDeclared)==85&&bank.Resources.Count==87,"complete original graph preserves undeclared/composite roots and all87 unique resources");
            foreach(var resource in bank.Resources)check(resource.Sha256==Sha(Resource(resource.Id)),"every archived ANIM leaf retains exact original bytes");
            int samples=0;
            foreach(var clipPin in pin.GetProperty("Clips").EnumerateArray())
            {
                int ordinal=clipPin.GetProperty("Ordinal").GetInt32();var clip=bank.OpenStrictPrimitive(ordinal);
                check(clip.Source.Id==Convert.ToUInt32(clipPin.GetProperty("ResourceId").GetString(),16)&&clip.Source.Sha256==clipPin.GetProperty("Sha256").GetString()
                    &&clip.KeyCount==clipPin.GetProperty("KeyCount").GetInt32()&&clip.Duration==clipPin.GetProperty("Duration").GetDouble()&&clip.RigidScale,"required donor clip remains its exact own strict source primitive");
                foreach(double time in Enumerable.Range(0,clip.KeyCount).Select(k=>k*clip.Tick).Append(clip.Duration*.37))
                {var pose=clip.Sample(time);samples++;check(pose.Rotations.Keys.Concat(pose.Translations.Keys).All(rig.Bones.ContainsKey)&&pose.Rotations.Values.Concat(pose.Translations.Values).All(v=>v.All(double.IsFinite)),"all required exact-key/off-grid poses remain finite and use only original33 bones");}
            }
            foreach(var root in bank.Roots.Where(r=>!r.StrictPlay))Reject(()=>bank.OpenStrictPrimitive(root.Ordinal),"composite graph is not silently a leaf");
            var wrongCinf=cinf.ToArray();wrongCinf[^1]^=1;Reject(()=>RetroCharacterAnimationArchive.Read(bankBytes,capture,wrongCinf,false),"wrong rig cannot bind captured bank");
            var changedAncs=ancs.ToArray();changedAncs[^1]^=1;Reject(()=>RetroAnimationGraphProof.Verify(descriptor,changedAncs),"wrong raw ANCS cannot bind graph");
            byte[] materialBytes=File.ReadAllBytes(Path.Combine(folder,stem+".materials.bin"));var materials=RetroCharacterMaterialArchive.Read(materialBytes);
            check(BinaryPrimitives.ReadUInt32LittleEndian(materialBytes.AsSpan(16))==(alpha?9u:10u)&&materials.Binding==new RetroCharacterMaterialArchive.Source(id,cmdlId,cskrId,0xc3af0235,0x5e058399,fingerprint)
                &&materials.CmdlSha256==Sha(cmdl)&&materials.CskrSha256==Sha(cskr)&&materials.GeometrySha256==Sha(glb)&&materials.Programs.Count==2,"new archive profile9/10 binds only own full drone resources/geometry/source edition");
            check(RetroCharacterMaterialArchive.Encode(materials.Binding,cmdl,cskr,Sha(glb),Resource).SequenceEqual(materialBytes),"archive deterministically reproduces from original own raw materialset/TXTR, without borrowed program authoring");
            Reject(()=>RetroCharacterMaterialArchive.Encode(materials.Binding with{Cmdl=alpha?0x0b6586cbu:0xef920604u},cmdl,cskr,Sha(glb),Resource),"crossappearance CMDL cannot bind own archive profile");
            Reject(()=>RetroCharacterMaterialArchive.Encode(materials.Binding with{SourceFingerprint=new string('0',40)},cmdl,cskr,Sha(glb),Resource),"wrong edition fingerprint cannot bind archive");
            foreach(var texturePin in pin.GetProperty("Textures").EnumerateArray())
            {
                uint textureId=Convert.ToUInt32(texturePin.GetProperty("Id").GetString(),16);byte[] raw=Resource(textureId);var decoded=materials.DecodeTexture(textureId);
                check(Sha(raw)==texturePin.GetProperty("Sha256").GetString()&&materials.RawTexture(textureId).SequenceEqual(raw)&&Sha(decoded.Pixels)==texturePin.GetProperty("DecodedTopRgbaSha256").GetString(),"every original raw TXTR and decoded top RGBA matches independent preimplementation literal pins");
            }
            foreach(int ordinal in new[]{0,1})
            {
                check(Sha(materials.RawProgram(ordinal))==pin.GetProperty("Materials")[ordinal].GetProperty("OriginalMaterialSha256").GetString(),"original full GX material record matches independent preimplementation literal byte hash");
                var program=CharacterSourceGxProgram.Compile(materials.ReadProgram(ordinal));
                check(program.DrawPass.ToString()=="Opaque"&&program.InitialRegisterDependencies==0
                    &&model.Triangles.Where((_,i)=>model.TriangleMaterial[i/3]==ordinal).SelectMany(c=>model.Skin![c.P]).Where(w=>w.Weight>0).Select(w=>w.Bone).Distinct().Count()==(ordinal==0?25:12),"raw drone program is CPU-supported opaque/no initial register dependency and original25/12 material palette fits; no GPU grant inferred");
            }
            var detached=materials.ReadProgram(0);detached.Stages[0].ColorIn[0]^=1;
            check(materials.ReadProgram(0).Stages[0].ColorIn[0]!=detached.Stages[0].ColorIn[0],"mutable program copies cannot alter original captured raw material authority");
            var firstTexture=materials.Textures[0];var pixelCopy=materials.DecodeTexture(firstTexture.ResourceId);pixelCopy.Pixels[0]^=1;
            check(Sha(materials.DecodeTexture(firstTexture.ResourceId).Pixels)==firstTexture.RgbaSha256,"mutable decoded RGBA copies do not alter immutable original TXTR archive");
            var locks=new List<FileStream>();
            try
            {
                foreach(string path in state.Files)locks.Add(new(path,FileMode.Open,FileAccess.Read,FileShare.None));
                for(int repeat=0;repeat<8;repeat++)
                {bank.OpenStrictPrimitive(13).Sample(repeat*.031);check(materials.DecodeTexture(firstTexture.ResourceId).Pixels.Length==firstTexture.DecodedBytes,"captured strict poses and materials work with all generation files exclusively locked, without lazy IO");}
            }
            finally{foreach(var held in locks)held.Dispose();}
            VerifyDocuments(folder,id,stem,alpha,fingerprint,model,cmdl,cskr,cinf,ancs,glb,descriptor,bankBytes,check);
            RetroCharacterPreviewExporter.ExportFromOwnedGame(source,individual,selectedIds:[id]);var separate=RetroCharacterPreviewExporter.InspectBundle(individual,id);
            check(separate.Files.Count==12&&state.Files.All(f=>HashFile(f)==HashFile(separate.Files.Single(g=>Path.GetFileName(g)==Path.GetFileName(f)))),"group and individual source-authority exports produce byte-identical12 components");
            foreach(string path in state.Files)
            {byte[] saved=File.ReadAllBytes(path),changed=saved.ToArray();changed[^1]^=1;try{File.WriteAllBytes(path,changed);check(RetroCharacterPreviewExporter.InspectBundle(imports,id).State!=ImportBundleState.Ready,"any changed own component refuses complete authoring admission");}finally{File.WriteAllBytes(path,saved);}}
            result.Add(new{Id=id,Triangles=1390,Joints=33,Programs=2,ExactGeometry=geometry,RequiredClipSamples=samples,MaterialSha256=Sha(materialBytes),BankSha256=Sha(bankBytes),GraphSha256=Sha(descriptor),Components=state.Files.OrderBy(Path.GetFileName).Select(f=>new{Name=Path.GetFileName(f),Sha256=HashFile(f)})});
        }
        CheckOldTroopers(imports,check);
        string mixed=Path.Combine(temporary,"mixed");var allIds=TrooperIds.Concat(DroneIds).ToArray();
        check(RetroCharacterPreviewExporter.ExportSourceGroupFromOwnedGame(source,allIds,mixed).Count==6,"owned Rev2 full same-source group publishes all six independent Prime bundles");
        foreach(string id in allIds)
        {
            var own=RetroCharacterPreviewExporter.InspectBundle(imports,id);var together=RetroCharacterPreviewExporter.InspectBundle(mixed,id);
            check(together.State==ImportBundleState.Ready&&own.Files.Count==together.Files.Count&&own.Files.All(f=>HashFile(f)==HashFile(together.Files.Single(g=>Path.GetFileName(g)==Path.GetFileName(f)))),"mixed archive union retains per-source exact byte authority: "+id);
        }
        Dictionary<string,string> Inventory()=>Directory.GetFiles(imports,"*",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(imports,p),HashFile,StringComparer.Ordinal);
        var committed=Inventory();void Preserved(string label){var current=Inventory();check(committed.Count==current.Count&&committed.All(p=>current.TryGetValue(p.Key,out string? value)&&value==p.Value),label);}
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();Reject(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,imports,cancelled.Token,selectedIds:DroneIds),"cancel before selected group reads source");}Preserved("pre-cancel preserves every good old component/current pointer");
        using(var cancelled=new CancellationTokenSource()){Reject(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,imports,cancelled.Token,new InlineProgress(stage=>{if(stage==ImportPublishStage.BeforeCommit)cancelled.Cancel();}),DroneIds),"cancel first selected BeforeCommit");}Preserved("BeforeCommit cancellation preserves all six good generations/current pointers and removes staged partial payload");
        Reject(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,imports,selectedIds:[DroneIds[0],"not-a-source"]),"partially unknown group refuses rather than partial publish");Preserved("invalid partial selection preserves all good imports");
        Reject(()=>RetroCharacterPreviewExporter.ExportFromOwnedGame(source,imports,selectedIds:[DroneIds[0],DroneIds[0]]),"duplicate source group refuses");Preserved("duplicate selection preserves all good imports");
        result.Add(new{Refusals=refusals,OldTrooperComponentsPreserved=52,OriginalGenerationsAndPointersPreservedOnCancellation=true});return result;
    }

    private sealed class InlineProgress(Action<ImportPublishStage> callback):IProgress<ImportPublishStage>{public void Report(ImportPublishStage value)=>callback(value);}
    private static void CheckSelections(Action<bool,string> check)
    {
        // Pure catalog/edition checks. No claim of running an original-edition
        // disc: only the verified owned Rev2 RVZ is opened by this fixture.
        string[] all=TrooperIds.Concat(DroneIds).ToArray();
        var originalExport=typeof(RetroCharacterPreviewExporter).GetMethods().Where(m=>m.Name==nameof(RetroCharacterPreviewExporter.ExportFromOwnedGame)).ToArray();
        check(originalExport.Length==1&&originalExport[0].GetParameters().Length==5,"original public five-parameter exporter remains unambiguous for frozen reflection callers");
        string[] Select(ExternalSourceEdition edition,IReadOnlyCollection<string>? ids=null,bool group=false)
            =>RetroCharacterPreviewExporter.SelectSourcesForEdition(ExternalGameKind.MetroidPrime,edition,ids,group).Select(s=>s.Id).Order(StringComparer.Ordinal).ToArray();
        void Equal(string[] actual,IEnumerable<string> expected,string why)=>check(actual.SequenceEqual(expected.Order(StringComparer.Ordinal)),why);
        void Reject(Action action,string why)
        {bool refused=false;try{action();}catch(Exception ex)when(ex is InvalidDataException or NotSupportedException or ArgumentException){refused=true;}check(refused,why);}
        Equal(Select(ExternalSourceEdition.OriginalRecipe),TrooperIds,"pure original Prime default retains four old Troopers and excludes unsupported drones");
        Equal(Select(ExternalSourceEdition.PrimeUsaRevision2),all,"pure Rev2 Prime default selects six supported independent sources");
        Equal(Select(ExternalSourceEdition.OriginalRecipe,all,true),TrooperIds,"pure full-game group filters original edition to its four supported Troopers");
        Equal(Select(ExternalSourceEdition.PrimeUsaRevision2,all,true),all,"pure full-game Rev2 group retains all six");
        Reject(()=>Select(ExternalSourceEdition.OriginalRecipe,all),"explicit all-six request cannot silently filter unsupported original drones");
        foreach(var edition in new[]{ExternalSourceEdition.OriginalRecipe,ExternalSourceEdition.PrimeUsaRevision2})
        {
            Equal(Select(edition,[TrooperIds[1],TrooperIds[3]]),[TrooperIds[1],TrooperIds[3]],"explicit supported subset is exact for either known edition");
            Reject(()=>Select(edition,TrooperIds,true),"partial game group cannot impersonate complete group authority");
            Reject(()=>Select(edition,null,true),"same-source group requires explicit full catalog rather than null authority");
            Reject(()=>Select(edition,[]),"empty explicit source selection refuses");
            Reject(()=>Select(edition,[TrooperIds[0],TrooperIds[0]]),"duplicate source selection refuses");
            Reject(()=>Select(edition,[TrooperIds[0],"darksamus-echoes"]),"foreign-game source cannot join a Prime selection");
            Reject(()=>Select(edition,["TROOPER-POWER"]),"noncanonical source ID spelling refuses");
            Reject(()=>Select(edition,[TrooperIds[0],"not-a-source"],true),"unknown source cannot grant group filtering authority");
        }
        Equal(Select(ExternalSourceEdition.PrimeUsaRevision2,[DroneIds[1]]),[DroneIds[1]],"explicit Rev2 Gamma subset remains exact");
        Reject(()=>Select(ExternalSourceEdition.OriginalRecipe,[DroneIds[1]]),"explicit original-edition drone refuses rather than returning an empty success");
        string[] echoes=["darksamus-echoes","darksamus-overcharged-echoes"];
        Equal(RetroCharacterPreviewExporter.SelectSourcesForEdition(ExternalGameKind.Echoes,ExternalSourceEdition.OriginalRecipe).Select(s=>s.Id).Order(StringComparer.Ordinal).ToArray(),echoes,"Echoes default catalog retains its two existing sources");
        Equal(RetroCharacterPreviewExporter.SelectSourcesForEdition(ExternalGameKind.Echoes,ExternalSourceEdition.OriginalRecipe,echoes,true).Select(s=>s.Id).Order(StringComparer.Ordinal).ToArray(),echoes,"explicit Echoes full group remains supported");
        Reject(()=>RetroCharacterPreviewExporter.SelectSourcesForEdition(ExternalGameKind.Echoes,ExternalSourceEdition.PrimeUsaRevision2),"PrimeRev2 edition cannot be relabeled Echoes");
        Reject(()=>Select((ExternalSourceEdition)int.MaxValue),"unknown edition refuses before media I/O");
    }
    private static void CheckOldTroopers(string imports,Action<bool,string> check)
    {
        using var witness=JsonDocument.Parse(BaselineWitness);
        foreach(var bundle in witness.RootElement.EnumerateArray())
        {
            string id=bundle.GetProperty("Id").GetString()!;var spec=RetroCharacterPreviewExporter.BundleSpec(id,ExternalSourceEdition.PrimeUsaRevision2);var state=RetroCharacterPreviewExporter.InspectBundle(imports,id);
            check(spec.Version==6&&spec.SourceFingerprint==bundle.GetProperty("SourceFingerprint").GetString()&&spec.RecipeHash==bundle.GetProperty("RecipeHash").GetString()&&state.State==ImportBundleState.Ready&&state.Files.Count==13,"old Trooper recipe/fingerprint remains exact frozen v6: "+id);
            foreach(var component in bundle.GetProperty("Components").EnumerateArray())check(HashFile(state.Files.Single(p=>Path.GetFileName(p)==component.GetProperty("Name").GetString()))==component.GetProperty("Sha256").GetString(),"original Trooper file remains byte-identical: "+id+"/"+component.GetProperty("Name").GetString());
        }
    }

    private static void VerifyDocuments(string folder,string id,string stem,bool alpha,string fingerprint,Cmdl model,byte[] cmdl,byte[] cskr,byte[] cinf,byte[] ancs,byte[] glb,byte[] graph,byte[] bank,Action<bool,string> check)
    {
        using var manifest=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,"characters.json")));var root=manifest.RootElement;
        check(root.GetProperty("format").GetInt32()==1&&root.GetProperty("models").GetArrayLength()==1,"manifest retains format1 and exactly one drone presentation asset");
        var entry=root.GetProperty("models")[0];
        check(entry.GetProperty("hunter").GetString()=="Weavel"&&entry.GetProperty("part").GetString()=="AlternateForm"&&entry.GetProperty("lod").GetInt32()==0
            &&entry.GetProperty("skinning").GetString()=="Weighted4"&&entry.GetProperty("poseSource").GetString()=="SourceRigPreview"&&entry.GetProperty("characterId").GetString()=="trooper"
            &&entry.GetProperty("skinSource").GetString()==(alpha?"MP1AlphaDrone":"MP1GammaDrone")&&entry.GetProperty("boneMap").EnumerateObject().Count()==0,"manifest owns exact Weavel drone appearance without a fabricated native bone map");
        check(entry.GetProperty("model").GetString()==stem+".source.glb"&&entry.GetProperty("provenance").GetString()==stem+".source.json"
            &&entry.GetProperty("provenanceSha256").GetString()==HashFile(Path.Combine(folder,stem+".source.json"))
            &&entry.GetProperty("sourceAnimationArchive").GetString()==stem+".animations.bin"&&entry.GetProperty("sourceAnimationArchiveSha256").GetString()==Sha(bank)
            &&(!entry.TryGetProperty("sourceCannonReceipt",out var cannon)||cannon.ValueKind==JsonValueKind.Null),"same-generation manifest hashes only own drone geometry/provenance/fullbank, no cannon capability");
        using var provenance=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder,stem+".source.json")));var p=provenance.RootElement;
        bool Text(string key,string expected)=>p.GetProperty(key).GetString()==expected;bool Number(string key,long expected)=>p.GetProperty(key).GetInt64()==expected;
        check(Number("format",1)&&Text("kind","generated-retro-drone-authoring-v1")&&Text("sourceRootContract","prime-drone-root3-parent2-v1")
            &&Text("sourceCharacterId",id)&&Text("sourcePart","drone")&&Text("sourceGameId","GM8E01")&&Text("sourceEdition","prime-usa-rev2")&&Text("sourceFingerprint",fingerprint)
            &&Text("sourceBasis","(-x,z,y)")&&Number("sourceRootBoneId",3)&&Number("sourceExternalParentId",2)&&!p.GetProperty("runtimeInstall").GetBoolean(),"provenance states independently closed33-bone raw drone authoring, exact source/root/basis, no runtime installation");
        check(Number("sourcePositionCount",1663)&&Number("sourceTriangleCount",1390)&&Number("sourceMaterialCount",2)&&Number("sourceBoneCount",33)
            &&Text("sourceCmdlSha256",Sha(cmdl))&&Text("sourceCskrSha256",Sha(cskr))&&Text("sourceCinfSha256",Sha(cinf))&&Text("sourceAncsSha256",Sha(ancs))
            &&Text("sourceGraphSha256",Sha(graph))&&Text("sourceAnimationArchiveSha256",Sha(bank))&&Text("sourceGlbSha256",Sha(glb)),"provenance exact counts/resources and whole-generation transport hashes reproduce independently");
    }

    private static object VerifyGlb(byte[] bytes,Cmdl model,RetroNativeSkeleton skeleton,Action<bool,string> check)
    {
        check(bytes.Length>=28&&BinaryPrimitives.ReadUInt32LittleEndian(bytes)==0x46546c67&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))==2
            &&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))==bytes.Length,"whole generated GLB2 declares exact byte extent");
        int jsonLength=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12)),binary=checked(28+jsonLength);
        check(jsonLength>0&&jsonLength%4==0&&binary<=bytes.Length&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16))==0x4e4f534a
            &&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24+jsonLength))==0x004e4942&&BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20+jsonLength))==bytes.Length-binary,"independent GLB chunks account for all bytes");
        using var document=JsonDocument.Parse(bytes.AsMemory(20,jsonLength));var r=document.RootElement;
        var bones=skeleton.Bones.Values.OrderBy(b=>b.Id).ToArray();var nodes=r.GetProperty("nodes");var skin=r.GetProperty("skins")[0];var extras=r.GetProperty("extras");
        check(extras.GetProperty("sourceRootContract").GetString()=="prime-drone-root3-parent2-v1"&&extras.GetProperty("sourceRootBoneId").GetInt32()==3&&extras.GetProperty("sourceExternalParentId").GetInt32()==2
            &&nodes.GetArrayLength()==34&&skin.GetProperty("joints").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(Enumerable.Range(0,33))
            &&r.GetProperty("materials").GetArrayLength()==2&&nodes[33].GetProperty("mesh").GetInt32()==0&&nodes[33].GetProperty("skin").GetInt32()==0,"closed full33 source rig plus identity mesh without padding/truncation or relabeling");
        var boneIndex=bones.Select((b,i)=>(b.Id,i)).ToDictionary(p=>p.Id,p=>p.i);
        for(int i=0;i<bones.Length;i++)
        {
            var b=bones[i];float x=b.X,y=b.Y,z=b.Z;
            if(skeleton.Bones.TryGetValue(b.Parent,out var parent)){x-=parent.X;y-=parent.Y;z-=parent.Z;}
            check(nodes[i].GetProperty("name").GetString()=="source_bone_"+b.Id+"_"+b.Name
                &&nodes[i].GetProperty("translation").EnumerateArray().Select(v=>v.GetSingle()).SequenceEqual(new[]{-x,z,y})
                &&nodes[i].GetProperty("children").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(bones.Where(c=>c.Parent==b.Id).Select(c=>boneIndex[c.Id])),"each original CINF identity/local bind/hierarchy is exact");
        }
        check(r.GetProperty("scenes")[0].GetProperty("nodes").EnumerateArray().Select(v=>v.GetInt32()).SequenceEqual(new[]{boneIndex[3],33}),"only original root3 and identity mesh are scene roots");
        ReadOnlySpan<byte> Access(int id,int component,string type,int count,int width)
        {
            var a=r.GetProperty("accessors")[id];var v=r.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];int offset=v.GetProperty("byteOffset").GetInt32(),length=v.GetProperty("byteLength").GetInt32();
            check(a.GetProperty("componentType").GetInt32()==component&&a.GetProperty("type").GetString()==type&&a.GetProperty("count").GetInt32()==count
                &&!a.TryGetProperty("byteOffset",out _)&&!v.TryGetProperty("byteStride",out _)&&length==checked(count*width)&&offset>=0&&offset<=bytes.Length-binary-length,"exact packed GLB accessor extent/type/count");
            return bytes.AsSpan(binary+offset,length);
        }
        float[] Floats(int id,string type,int count,int width)
        {
            var raw=Access(id,5126,type,count,width*4);var values=new float[count*width];for(int i=0;i<values.Length;i++)values[i]=BinaryPrimitives.ReadSingleLittleEndian(raw[(i*4)..]);
            check(values.All(float.IsFinite),"finite original GLB attribute values");return values;
        }
        var inverse=Floats(skin.GetProperty("inverseBindMatrices").GetInt32(),"MAT4",33,16);
        for(int i=0;i<bones.Length;i++){var b=bones[i];check(inverse.AsSpan(i*16,16).SequenceEqual(new[]{1f,0,0,0,0,1,0,0,0,0,1,0,b.X,-b.Z,-b.Y,1}),"original full33 inverse binds match exact CINF globals in converted basis");}
        var seen=new HashSet<int>();int corners=0,primitives=0;
        foreach(var primitive in r.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())
        {
            primitives++;int material=primitive.GetProperty("material").GetInt32();var attributes=primitive.GetProperty("attributes");
            var ids=primitive.GetProperty("extras").GetProperty("sourceTriangleIds").EnumerateArray().Select(v=>v.GetInt32()).ToArray();int count=ids.Length*3;
            check(material==primitive.GetProperty("extras").GetProperty("sourceMaterialOrdinal").GetInt32()&&ids.SequenceEqual(Enumerable.Range(0,1390).Where(i=>model.TriangleMaterial[i]==material)),"every original whole triangle retains global ID/material ordinal/order");
            var positions=Floats(attributes.GetProperty("POSITION").GetInt32(),"VEC3",count,3);var normals=Floats(attributes.GetProperty("NORMAL").GetInt32(),"VEC3",count,3);
            var uv=Floats(attributes.GetProperty("TEXCOORD_0").GetInt32(),"VEC2",count,2);var weights=Floats(attributes.GetProperty("WEIGHTS_0").GetInt32(),"VEC4",count,4);
            var joints=Access(attributes.GetProperty("JOINTS_0").GetInt32(),5123,"VEC4",count,8);var indices=Access(primitive.GetProperty("indices").GetInt32(),5125,"SCALAR",count,4);
            for(int f=0;f<ids.Length;f++)
            {
                check(seen.Add(ids[f]),"no source face duplicated between material groups");
                for(int c=0;c<3;c++)
                {
                    int v=f*3+c;var source=model.Triangles[ids[f]*3+c];int p=source.P*3,n=source.N*3,t=source.T0*2;
                    check(positions.AsSpan(v*3,3).SequenceEqual(new[]{-model.Positions[p],model.Positions[p+2],model.Positions[p+1]})
                        &&normals.AsSpan(v*3,3).SequenceEqual(new[]{-model.Normals[n],model.Normals[n+2],model.Normals[n+1]})
                        &&uv.AsSpan(v*2,2).SequenceEqual(new[]{model.Uv0[t],model.Uv0[t+1]})&&BinaryPrimitives.ReadUInt32LittleEndian(indices[(v*4)..])==v,"all original expanded P/N/T0 corners and winding match independently parsed CMDL exactly");
                    var positive=model.Skin![source.P].Where(w=>w.Weight>0).ToArray();
                    for(int j=0;j<4;j++)
                    {
                        int joint=BinaryPrimitives.ReadUInt16LittleEndian(joints[(v*8+j*2)..]);
                        check(j<positive.Length?joint<33&&bones[joint].Id==positive[j].Bone&&weights[v*4+j]==positive[j].Weight:joint==0&&weights[v*4+j]==0,"every own source bone ID/weight preserved exactly with only zero padding");
                    }
                    corners++;
                }
            }
        }
        check(primitives==2&&seen.Count==1390&&corners==4170,"complete two-material1390-face4170-corner source geometry");
        return new{Sha256=Sha(bytes),Nodes=34,Joints=33,Primitives=primitives,Triangles=seen.Count,ExactCorners=corners};
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static string Sha(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
