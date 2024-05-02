using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Transactions;
using System.Drawing;

#pragma warning disable CS8622

namespace Miluva
{
    // 99.98055251% from the 22.6 GB sized PGN File
    // ~310k Games with 2100+ Elo (4.2017-File)
    public static class TLMDatabase
    {
        private static string[] DATABASE = Array.Empty<string>();
        private static int DATABASE_SIZE = 0;

        private static string[] DATABASEV2 = Array.Empty<string>();
        private static int DATABASEV2_SIZE = 0;

        public const int chunkSize = 10_000_000;
        public const int threadMargin = 10000;
        public const int minElo = 2100;
        public const int minMoves = 12;

        public const int minMovesInDatabaseForMatch = 5;
        public const double minPercentage = 0.035;

        public static int completedThreads = 0;
        public static int games = 0;

        private const string lichessDatabasePath = @"C:\Users\tpmen\Downloads\lichess_db_standard_rated_2017-04.pgn\lichess_db_standard_rated_2017-04.pgn";
        private static List<TLMDatabaseThreadObject> threadTasks = new List<TLMDatabaseThreadObject>();
        private static List<string> validPGNs = new List<string>();
        public static List<string> processedPGNs = new List<string>();
        private static IBoardManager? bM;
        private static Dictionary<char, int> pieceTypeDict = new Dictionary<char, int>();

        private static Random databaseRNG = new Random();

        private static int[] rankArr = new int[64]
        {
            0, 0, 0, 0, 0, 0, 0, 0,
            1, 1, 1, 1, 1, 1, 1, 1,
            2, 2, 2, 2, 2, 2, 2, 2,
            3, 3, 3, 3, 3, 3, 3, 3,
            4, 4, 4, 4, 4, 4, 4, 4,
            5, 5, 5, 5, 5, 5, 5, 5,
            6, 6, 6, 6, 6, 6, 6, 6,
            7, 7, 7, 7, 7, 7, 7, 7
        };
        private static bool[] skippedChars = new bool[512];

        public static void InitDatabase()
        {
            string tPath2 = PathManager.GetTXTPath("DATABASES/TLM_DB1");
            DATABASE = File.ReadAllLines(tPath2);
            DATABASE_SIZE = DATABASE.Length;
            DATABASEV2 = File.ReadAllLines(PathManager.GetTXTPath("DATABASES/TLM_DB2"));
            DATABASEV2_SIZE = DATABASEV2.Length;
            bM = BOT_MAIN.boardManagers[ENGINE_VALS.PARALLEL_BOARDS - 1];

            Console.WriteLine("[TLM_DB] LOADED IN");
        }

        public static void LoadNN()
        {
            NeuralNetwork neuNet = new NeuralNetwork(

                NeuronFunctions.Sigmoid, NeuronFunctions.SigmoidDerivative, // STANDARD NEURON FUNCTION
                OUTPUT_SIGMOID, OUTPUT_SIGMOID_DERIVATIVE, // OUTPUT NEURON FUNCTION
                NeuronFunctions.SquaredDeviation, NeuronFunctions.SquareDeviationDerivative, // DEVIATION FUNCTION

                768, 16, 8, 7, 1);

            neuNet.LoadNNFromString(
                File.ReadAllText(PathManager.GetTXTPath("OTHER/CUR_NN"))
            );

            BoardManager tBM = new BoardManager("r1bqk1nr/ppp2ppp/2np4/2b1p3/2B1P3/1P1P1N2/P1P2PPP/RNBQK2R b KQkq - 0 5");

            tBM.LoadFenString("r1bq1rk1/ppp2ppp/2np1n2/2b1p3/8/8/5R2/R1B3K1 b Q - 0 7");

            ulong[] tULArr = new ulong[12];

            for (int i = 0; i < 64; i++)
            {
                int tPT = tBM.pieceTypeArray[i];
                if (ULONG_OPERATIONS.IsBitOne(tBM.whitePieceBitboard, i)) tULArr[tPT - 1] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT - 1], i);
                else if (ULONG_OPERATIONS.IsBitOne(tBM.blackPieceBitboard, i)) tULArr[tPT + 5] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT + 5], i);
            }

            double[] tinps = NNUE_DB_DATA.ULONGARRS_TO_NNINPUTS(tULArr);
            Console.WriteLine((neuNet.CalculateOutputs(tinps)[0] - 1) * 400);
        }

        private static double[] EvNNInp = new double[16];
        // Math.Net Matrix Multiplication: 16 > 16 (just mult) 2.7s/10mil -> ~1,234,567 e/s
        // Barebone Double Alignment: 16 > 16 (+ bias & parametric ReLU) 0.69s/10mil -> ~5,000,000 e/s

        public static double EfficientProcessOfNN2()
        {
            double A0 = EvNNInp[0], A1 = EvNNInp[1], A2 = EvNNInp[2], A3 = EvNNInp[3], A4 = EvNNInp[4], A5 = EvNNInp[5], A6 = EvNNInp[6], A7 = EvNNInp[7], A8 = EvNNInp[8], A9 = EvNNInp[9], A10 = EvNNInp[10], A11 = EvNNInp[11], A12 = EvNNInp[12], A13 = EvNNInp[13], A14 = EvNNInp[14], A15 = EvNNInp[15];
            double B0 = EvNNF1(1.2939124758683866 * A0 + .7144785930176016 * A1 - .33053041916129805 * A2 + .5885073379690412 * A3 + .2327717563222625 * A4 + .3927689319298945 * A5 + .748892195383743 * A6 + .6526735194176022 * A7 - .6279161769500395 * A8 + .6693487674606016 * A9 + 1.2918812958967623 * A10 - .40150076821467634 * A11 - .3071596904641261 * A12 + .6506905562563978 * A13 - 1.0280382533403756 * A14 + .3788553210414526 * A15 + .20012883795287567);
            double B1 = EvNNF1(-.5251675439411643 * A0 + .02916012640154912 * A1 + .5195519161131261 * A2 + .784696558380395 * A3 - .48036868393613824 * A4 - .09638798548844359 * A5 - .6441701444569078 * A6 + .7615376317155985 * A7 - 1.1276483264062558 * A8 + .37795921558744866 * A9 + 1.0114993815942184 * A10 + .9915546138143065 * A11 - .4139508971255122 * A12 - .09133128503848426 * A13 - 1.0293222057765548 * A14 - .9198978958712254 * A15 - .7541271952962914);
            double B2 = EvNNF1(.17204382727194512 * A0 + .4872156057424523 * A1 + .09264884408461405 * A2 + .23801635710162514 * A3 - .9875164157499237 * A4 + .9945108456705899 * A5 - 1.1026369947079877 * A6 - 1.1097860645097066 * A7 + .1261131345625245 * A8 + .7294696725308117 * A9 + .8566891915023447 * A10 + .29091732165312273 * A11 - 1.3441287249794944 * A12 + 1.0885412503418315 * A13 - .808767966865825 * A14 - 1.498690984653565 * A15 + .35178579193592185);
            double B3 = EvNNF1(-.4255666254870994 * A0 - .36284906481567236 * A1 + .1838657460710973 * A2 - .20169850100591455 * A3 - .0016588012029849896 * A4 + 1.4448323700706858 * A5 - 1.4006495613992669 * A6 - .2908654082661265 * A7 + .046968461718182496 * A8 - 1.3032507226926875 * A9 - .3869505573901816 * A10 - .576758942714624 * A11 + .733950948138724 * A12 - .8165448928888854 * A13 - .7829774638420676 * A14 + .8271138932054339 * A15 - 1.1938014761139502);
            double B4 = EvNNF1(-1.7752744872132218 * A0 - .38179690865280363 * A1 - .23954240320655673 * A2 + .21936827874024445 * A3 - 2.3769225461378642 * A4 + .8111980311628033 * A5 + .747424573972328 * A6 - .6651302392800124 * A7 - .20667686816392214 * A8 + .7850012599914834 * A9 - .7001847810611918 * A10 + .616913387197615 * A11 + .4918923203221846 * A12 - 1.0583486102928992 * A13 - .4949096458648162 * A14 - .6566395346679728 * A15 - .070583800534248);
            double B5 = EvNNF1(.23788739760537386 * A0 - 1.2350522951489635 * A1 - .1693994274128821 * A2 - 1.8211067461733799 * A3 - .20759199590675287 * A4 + .3522044009835585 * A5 - .7895285840297781 * A6 + 1.3919916183905996 * A7 - .516066249698682 * A8 + 1.1753297940856902 * A9 - .0036309605439236183 * A10 + .044145480468437116 * A11 + .1288745788264109 * A12 - .7021613279778405 * A13 + .5936177726302553 * A14 + .9922458787114133 * A15 - .29778183515333767);
            double B6 = EvNNF1(-.607204120301669 * A0 + .504181327943233 * A1 + .5408933832341137 * A2 + .6852238021888096 * A3 - .8694204755462717 * A4 - 2.0020954626637915 * A5 + .6072123051515698 * A6 + .6478561097415824 * A7 - .38878600005143366 * A8 - 1.846708375723111 * A9 + .5244790392487505 * A10 + .2952851132825455 * A11 + .09003046911205426 * A12 + .5582006409413914 * A13 - .9941700966273607 * A14 + .01726911404559954 * A15 + .02289710578361245);
            double B7 = EvNNF1(-.8280723170638278 * A0 + 1.4948039053341877 * A1 - .7167415116896612 * A2 - 2.0743703436235346 * A3 + .13124510787653154 * A4 + .17028318419757565 * A5 - .10584454545793598 * A6 + .4263432173593843 * A7 + 1.4286596871663129 * A8 - .05493018503719309 * A9 + .10340877868816728 * A10 - 1.5704340483994672 * A11 + .6741483906687511 * A12 - .1026861096563212 * A13 - 1.3005739314227531 * A14 + .9806039488699464 * A15 + .6226755824596502);
            double B8 = EvNNF1(.09518458028849523 * A0 - 1.3348385680403336 * A1 - 1.2097181970946587 * A2 + .6169263322312254 * A3 + .7515044008987602 * A4 - 1.1567835441888321 * A5 + .20191123193424593 * A6 - 1.090357433399104 * A7 + .9001690781414188 * A8 - .6798765819642757 * A9 + .9510786466563118 * A10 - 1.0510359487781242 * A11 - .14730614496811373 * A12 + .6169157696337026 * A13 - .929140785651846 * A14 + .6148252005680904 * A15 - 1.0450309200173584);
            double B9 = EvNNF1(.5094077050008651 * A0 - .1595904931634793 * A1 - .085927866720952 * A2 - .9985215177777437 * A3 - .13264382771239147 * A4 - .09247893671191433 * A5 - 1.242037664256496 * A6 + 1.171470278757055 * A7 - .3384263087886569 * A8 - .4543705013120933 * A9 - .8541750317681269 * A10 + .9194533758248042 * A11 + .07854481776023466 * A12 + .836587562899599 * A13 + .3108337832533169 * A14 - .007425396466211433 * A15 - .47403900362003726);
            double B10 = EvNNF1(-.22032731018032933 * A0 - .3793651102025629 * A1 + .7002503973741587 * A2 + .3401837705466615 * A3 + .24586680470475328 * A4 + .7610007021026415 * A5 + .8704193624086144 * A6 + .30714891712141223 * A7 - .8622619867099882 * A8 + 1.0785318356830613 * A9 - .2659835001622739 * A10 + .05687892399925499 * A11 - .36496596120663866 * A12 - .8368781890930398 * A13 - .41748508735991396 * A14 - .2595875598399085 * A15 - .8018462298127501);
            double B11 = EvNNF1(.6779972550885937 * A0 + .5499883045837233 * A1 + .8121852691570575 * A2 - .27568907388536756 * A3 + .4051178288012074 * A4 - 1.4149590096942706 * A5 + .05736774563887564 * A6 - .6525002237201808 * A7 + 1.5273315515976211 * A8 + .027956689231701616 * A9 - 1.3603893600550412 * A10 - .6640407293541395 * A11 - .552715469861783 * A12 - .3641354520557283 * A13 - .42996618653393937 * A14 + 1.2108498248299548 * A15 - .5707794521553853);
            double B12 = EvNNF1(-.13899559706054798 * A0 + .057281693056448674 * A1 - 1.3567362144432153 * A2 + .36974623535893575 * A3 - .11334634881145728 * A4 - 1.149150070890495 * A5 - 1.042646520988977 * A6 - .1404528804581147 * A7 - .045286260101313966 * A8 + .4561096974457888 * A9 + .6572557316235605 * A10 + .7297122248657536 * A11 - 1.1016390509173013 * A12 - .952567088283 * A13 - .7031377434075385 * A14 - 1.0358707788384387 * A15 + .7277280401312023);
            double B13 = EvNNF1(.5336646127848031 * A0 + 1.6362474355342072 * A1 - 2.083067035518955 * A2 + .35897937427824306 * A3 + .8257782187903588 * A4 + .1473426797730498 * A5 - .12729188776835237 * A6 + .8515418137848204 * A7 + .6561408326955902 * A8 - .11245168152658001 * A9 - .08084616474694505 * A10 + .9377459869481372 * A11 + .5815275701125212 * A12 + .7037608808949753 * A13 - .6727493998988614 * A14 - .5597606176452271 * A15 + .30627398109292575);
            double B14 = EvNNF1(.8607698049182083 * A0 - .7824317690456337 * A1 - .49247386062330817 * A2 - .6010574043446629 * A3 - .7803128681126518 * A4 + 1.160739191058728 * A5 + .07701187282883191 * A6 + .6818257522385021 * A7 - .1105610343751127 * A8 - .8588765316138365 * A9 - 1.4057325373149074 * A10 + .22825059956763202 * A11 - 1.2322127469779618 * A12 + .459985925313804 * A13 - .02341015559912013 * A14 - .8831682256231324 * A15 + .6799816175929229);
            double B15 = EvNNF1(1.2277958749304922 * A0 - .21073478912750682 * A1 + .3371033705008725 * A2 - 1.128479110405675 * A3 + .7619855986591171 * A4 + .7507507517612837 * A5 + .5135144945600948 * A6 + .7662121806291072 * A7 - 1.1253360637117475 * A8 + .9407692090809474 * A9 + .3367552370806927 * A10 - 1.1387442776529226 * A11 - .4926892719289247 * A12 - .6357420866734486 * A13 - .9587499919715513 * A14 - 1.1158301404908015 * A15 - .8226540560516297);
            double C0 = EvNNF1(-.35724918648929443 * B0 - .39163346923279563 * B1 - .5346354484693351 * B2 - .8282489657370495 * B3 + .75978632035218 * B4 + .1278234662983048 * B5 + .873879760922478 * B6 - 1.433801615061863 * B7 - 1.4507607447759026 * B8 - .6596013145778689 * B9 - .002089065237445665 * B10 - .09322076337303528 * B11 - 1.0640423354712902 * B12 - .703103810161366 * B13 - .8446084553041362 * B14 + 1.460815625930577 * B15 + .4728837623919909);
            double C1 = EvNNF1(-.22912234342736693 * B0 - .5232626879249168 * B1 - .5616630274284575 * B2 - 1.61032161447203 * B3 + .6767566515695113 * B4 + .6833384711540798 * B5 - .7695118923170944 * B6 - .5519459626227795 * B7 + .9346439623265069 * B8 + .5915881309713791 * B9 - .16919172488880502 * B10 - 1.3024978188987397 * B11 - 1.4014117976396436 * B12 + .13697199476782643 * B13 + .626780697723774 * B14 - 1.424226014308863 * B15 - .36360790497492734);
            double C2 = EvNNF1(.6101607733132933 * B0 + 1.0682648898392482 * B1 + 1.112702575107594 * B2 - .6016822257312276 * B3 + .658987445367127 * B4 - .11186328363207174 * B5 - 1.3246589314279509 * B6 - .7426476232094859 * B7 + 1.1440748272940764 * B8 + .7157448267011045 * B9 - .6534831634748303 * B10 + .5295295596931714 * B11 + .5839448097701005 * B12 - 1.9103400623838256 * B13 + 1.0262088384474062 * B14 - .9243031189692708 * B15 - .7310704083002796);
            double C3 = EvNNF1(-1.46807266560381 * B0 - .3459119396085605 * B1 - .7482268916530818 * B2 + .8251506806920794 * B3 - .784207606405777 * B4 - .42767483903323966 * B5 - .7638300787382759 * B6 - .4486447502377349 * B7 + .23899209794571452 * B8 + .4285331120275911 * B9 + .48068800834836767 * B10 + 1.4538144471018484 * B11 - .07892913427405951 * B12 - 2.236319575001519 * B13 + .7826769514214652 * B14 + .538748334784718 * B15 + .5594732374870587);
            double C4 = EvNNF1(-.4384762506011758 * B0 - .0949464930326346 * B1 + .8959573457590285 * B2 - .8212184242246426 * B3 + .20307471665142046 * B4 + .2602621816200917 * B5 + .7965407336364749 * B6 - .6624951471787777 * B7 - 1.1870201283246566 * B8 + .8415231046846449 * B9 - .16851409897431144 * B10 + .48790159736823246 * B11 - .838535178922647 * B12 + .14557996533955447 * B13 - .43627235585561425 * B14 + .8690867381835783 * B15 - 1.0241227372259494);
            double C5 = EvNNF1(-1.761159021978136 * B0 - .7383899128270007 * B1 - .29412388670335143 * B2 + 1.3614775908263996 * B3 - .48718350287708506 * B4 - .8724959992943854 * B5 - .9931026441634874 * B6 + .4911563982933518 * B7 - .9920358757573798 * B8 - 1.0610537808428095 * B9 - .4217707075704957 * B10 + .3761292383153669 * B11 + .716198291637229 * B12 - .7818137308092747 * B13 + .2597083619312224 * B14 + .5243675175564089 * B15 + .5829375229217898);
            double C6 = EvNNF1(-.5299267986149926 * B0 + 1.1236674941527551 * B1 - .6013721330290317 * B2 + .07164092308088514 * B3 - 1.3929597933572235 * B4 + 1.7089226288288522 * B5 + 1.3098930359464998 * B6 + .7483491651834436 * B7 + .9535147323075123 * B8 - .2117248735481771 * B9 - 1.3206167787607574 * B10 + .6018241305542019 * B11 + .5036467023297091 * B12 + .6080927990850712 * B13 - 1.750941046708813 * B14 + .2195638904693054 * B15 - .9189772430298926);
            double C7 = EvNNF1(-1.0602650616728273 * B0 + .5822686351761024 * B1 - .613541670346213 * B2 - .03531632924236996 * B3 + .21783338882075348 * B4 - 1.5930798783797933 * B5 - .866347960647118 * B6 + .23037908577024307 * B7 + .6417207066200955 * B8 - 1.4310414446151931 * B9 + .20818715804086915 * B10 + .23636183091994165 * B11 - .05915142678912938 * B12 - .01336747610513445 * B13 + .9816443134288294 * B14 - 1.4465915169119623 * B15 + .3043468014815169);
            return EvNNF2(-1.334734807318012 * C0 - .9700065651193572 * C1 - 1.468390289121036 * C2 + 1.8293397735687222 * C3 - .6800303922632096 * C4 - 1.1871399203595059 * C5 + .9119948658732946 * C6 + 1.2239918631534419 * C7 - .26164839918250454);
        }

        public static double EvNNF1(double val)
        {
            if (val < 0d) return 0.4d * val;
            return val;
        }
        public static int EvNNF2(double val)
        {
            return (int)((1d / (1d + Math.Exp(-val)) - .5) * 1000d);
        }

        public static double ParametricReLU(double val)
        {
            //return val < 0d ? 0.1d * val : val;

            if (val < 0d) return 0.1d * val;
            return val;
        }


        private static NeuralNetwork neuNet2 = new NeuralNetwork(

        NeuronFunctions.ParametricReLU, NeuronFunctions.ParametricReLUDerivative, // STANDARD NEURON FUNCTION
        NNSigmoid, NNSigmoidDerivative, // OUTPUT NEURON FUNCTION
        NeuronFunctions.SquaredDeviation, NeuronFunctions.SquareDeviationDerivative, // DEVIATION FUNCTION

        768, 16, 16, 8, 1);


        public static double NNSigmoid(double val)
        {
            return 1d / (1d + Math.Exp(-0.00625 * val));
        }

        public static double NNSigmoidDerivative(double val)
        {
            return 0.00625 * (val = NNSigmoid(val)) * (1 - val);
        }

        public static double NNSpecialSquaredDeviation(double val, double expectedVal)
        {
            if (Math.Sign(val) == Math.Sign(expectedVal)) return (val - expectedVal) * (val - expectedVal);
            return 10 * (val - expectedVal) * (val - expectedVal);
        }

        public static double NNSpecialSquareDeviationDerivative(double val, double expectedVal)
        {
            if (Math.Sign(val) == Math.Sign(expectedVal)) return 2 * (val - expectedVal);
            return 20 * (val - expectedVal);
        }

        public static void LoadNN2()
        {
            neuNet2.LoadNNFromString(
                File.ReadAllText(PathManager.GetTXTPath("OTHER/CUR_NN"))
            );

            neuNet2.GetEvaluationFunctionStr();

            double[] tinps = ULONGARRS_TO_NNINPUTS(GetNNParamsFromFEN(@"2r4k/p4bp1/4pq2/1p1p4/2n2P2/P2B4/1P5P/1K1RR3 w - - 8 28"));

            Console.WriteLine(
                ConvertNNSigmoidtoCPVals(neuNet2.CalculateOutputs(tinps)[0])
            );
        }

        public static void TrainNN2()
        {
            const int SIZE_OF_TEST_DATA = 10000;

            neuNet2.GenerateRandomNetwork(new System.Random(), -1f, 1f, -1f, 1f);
            Console.WriteLine("Generated NN");

            List<TrainingData> tTrainingData = new List<TrainingData>();

            string[] data = File.ReadAllLines(@"C:\Users\tpmen\Downloads\chessData.csv\chessData.csv");
            int skippedLines = 0, ccount = 0;

            int[] cpcategories = new int[6];
            bool[] bcpcategories = new bool[6];

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].Length < 10)
                {
                    skippedLines++;
                    continue;
                }

                int tCPV = GetCPValFromTXTLineV2(data[i]);

                int tcomp = Math.Abs(tCPV), tI;
                if (tcomp < 50) tI = 0;
                else if (tcomp < 100) tI = 1;
                else if (tcomp < 200) tI = 2;
                else if (tcomp < 400) tI = 3;
                else if (tcomp < 800) tI = 4;
                else tI = 5;

                if (cpcategories[tI] >= 80_000)
                {
                    if (!bcpcategories[tI])
                    {
                        bcpcategories[tI] = true;
                        Console.WriteLine(tI + ": " + i);
                    }
                    if (ccount >= 480_000)
                    {
                        Console.WriteLine(i);
                        break;
                    }
                    continue;
                }

                ccount++;
                cpcategories[tI]++;

                tTrainingData.Add(new TrainingData(ULONGARRS_TO_NNINPUTS(GetNNParamsFromFEN(data[i])), new double[] { ConvertCPValsToNNSigmoid(tCPV) }));
            }

            Console.WriteLine(skippedLines);
            Console.WriteLine(tTrainingData.Count);

            TrainingData[] TrainingDataArr = RearrangeTrainingData(tTrainingData);

            int tL = TrainingDataArr.Length;
            TrainingData[] TrainingDataArrPureTraining = new TrainingData[tL - SIZE_OF_TEST_DATA];
            TrainingData[] TrainingDataArrGeneralizationTest = new TrainingData[SIZE_OF_TEST_DATA];
            
            for (int i = 0; i < tL; i++)
            {
                if (i < SIZE_OF_TEST_DATA) TrainingDataArrGeneralizationTest[i] = TrainingDataArr[i];
                else TrainingDataArrPureTraining[i - SIZE_OF_TEST_DATA] = TrainingDataArr[i];
            }

            TrainingData[][] TrainingDataBatches = TrainingData.CreateMiniBatches(TrainingDataArrPureTraining, 40);
            int batchCount = TrainingDataBatches.Length;
            Console.WriteLine("BatchCount: " + batchCount);

            int curBatch = 0;
            int epoch = 0;
            double recordDeviation = 1000d;

            Stopwatch sw = Stopwatch.StartNew();

            for (int i = 0; i < 7_500_000; i++)
            {
                neuNet2.GradientDescent(TrainingDataBatches[curBatch], 50, false);

                if (i % batchCount == 0)
                {
                    Console.WriteLine(sw.Elapsed);
                    LogManager.LogText("\n\n\n<<< EPOCH " + ++epoch + " >>> \n");

                    (double, double) testDatasetDeviation = neuNet2.CalculateDeviationTwice(TrainingDataArrGeneralizationTest);
                    Console.WriteLine("EPOCH " + epoch + ": " + testDatasetDeviation.Item1 + " | " + testDatasetDeviation.Item2);

                    if (recordDeviation > testDatasetDeviation.Item1)
                    {
                        neuNet2.RawLog();
                        recordDeviation = testDatasetDeviation.Item1;
                        //(double, double) tdev = neuNet2.CalculateDeviationTwice(TrainingDataArrPureTraining);
                        //Console.WriteLine(tdev.Item1 + " | " + testDatasetDeviation.Item2);
                    }
                }
                if (++curBatch == batchCount) curBatch = 0;
            }

            Console.WriteLine(neuNet2.CalculateDeviation(TrainingDataArr));

            //neuNet2.RawLog();
        }

        private static double[] ULONGARRS_TO_NNINPUTS(ulong[] pULArrs)
        {
            int tL = pULArrs.Length, a = 0;
            double[] rArr = new double[tL * 64];
            for (int j = 0; j < tL; j++)
                for (int i = 0; i < 64; i++)
                    rArr[a++] = (pULArrs[j] >> i) & 1;
            return rArr;
        }

        public static void PlayThroughDatabase2(BoardManager pBM)
        {
            const int MAX_GAME_AMOUNT = 100000;

            pBM.LoadFenString(ENGINE_VALS.DEFAULT_FEN);
            pBM.SetJumpState();

            List<TrainingData> tTrainingData = new List<TrainingData>();

            //ulong[] ttULArr = new ulong[12];

            Stopwatch swww = Stopwatch.StartNew();
            //
            //for (int i = 0; i < 1_000_000; i++)
            //{
            //    double[] tArr = NNUE_DB_DATA.ULONGARRS_TO_NNINPUTS(ttULArr);
            //}
            //
            //sw.Stop();

            //Console.WriteLine(DATABASE_SIZE);

            List<string> tempFENS = new List<string>();
            int[,,,] pieceTypeMatchups = new int[5, 5, 5, 3];
            int[,,,] pieceTypeMatchupsMoves = new int[5, 5, 5, 3];
            int ccount = 0, cccount = 0;

            for (int e = 0; e < DATABASE_SIZE && e < MAX_GAME_AMOUNT; e++)
            {

                List<string> temptempFENS = new List<string>();

                pBM.LoadJumpState();
                string pStr = DATABASE[e];
                int tL = pStr.Length;
                List<char> tchars = new List<char>();
                List<ulong[]> gameMoveInputs = new List<ulong[]>();

                bool[,,,] gameBlock = new bool[5, 5, 5, 3];
                bool b = false;

                for (int cpos = 1; cpos < tL; cpos++)
                {

                    char ch = pStr[cpos];
                    if (ch == ',')
                    {
                        int tMoveHash = NuCRe.GetNumber(new String(tchars.ToArray()));
                        List<Move> tMoves = new List<Move>();
                        pBM.GetLegalMoves(ref tMoves);
                        int ti_tL = tMoves.Count;
                        for (int m = 0; m < ti_tL; m++)
                        {
                            Move tM = tMoves[m];
                            if (tM.moveHash == tMoveHash)
                            {
                                pBM.PlainMakeMove(tM);

                                //ulong[] tULArr = new ulong[12];

                                int[] pieceTypeCounts = new int[7];

                                for (int i = 0; i < 64; i++)
                                {
                                    int tPT = pBM.pieceTypeArray[i];
                                    pieceTypeCounts[tPT]++;
                                    //if (ULONG_OPERATIONS.IsBitOne(pBM.whitePieceBitboard, i)) tULArr[tPT - 1] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT - 1], i);
                                    //else if (ULONG_OPERATIONS.IsBitOne(pBM.blackPieceBitboard, i)) tULArr[tPT + 5] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT + 5], i);
                                }

                                if (pieceTypeCounts[2] > 4 || pieceTypeCounts[3] > 4 || pieceTypeCounts[4] > 4 || pieceTypeCounts[5] > 2)
                                {
                                    if (!b) cccount++;
                                    b = true;
                                    break;
                                }

                                if (pieceTypeCounts[2] == 0 && pieceTypeCounts[3] == 0 && pieceTypeCounts[4] == 0 && pieceTypeCounts[5] == 0)
                                    tempFENS.Add(pBM.CreateFenString());
                                if (!gameBlock[pieceTypeCounts[2], pieceTypeCounts[3], pieceTypeCounts[4], pieceTypeCounts[5]]) {
                                    pieceTypeMatchups[pieceTypeCounts[2], pieceTypeCounts[3], pieceTypeCounts[4], pieceTypeCounts[5]]++;
                                    gameBlock[pieceTypeCounts[2], pieceTypeCounts[3], pieceTypeCounts[4], pieceTypeCounts[5]] = true;
                                    ccount++;
                                }
                                pieceTypeMatchupsMoves[pieceTypeCounts[2], pieceTypeCounts[3], pieceTypeCounts[4], pieceTypeCounts[5]]++;
                                // gameMoveInputs.Add(tULArr);


                                //for (int j = 0; j < 12; j++)
                                //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tULArr[j]));

                                break;
                            }
                        }

                        tchars.Clear();
                    }
                    else tchars.Add(ch);
                }

                //NNUE_DB_DATA tNDD = new NNUE_DB_DATA(gameMoveInputs, Convert.ToInt32(new String(tchars.ToArray())));
                //tTrainingData.AddRange(tNDD.TrainingData);

                //if (temptempFENS.Count != 0) tempFENS.Add(temptempFENS[databaseRNG.Next(0, temptempFENS.Count)]);

                if (e % 1000 == 0) Console.WriteLine(e + " Done!");
            }

            Console.WriteLine(tTrainingData.Count);
            Console.WriteLine(swww.ElapsedMilliseconds);

            for (int i = 0; i < 5; i++)
            {
                for (int i2 = 0; i2 < 5; i2++)
                {
                    for (int i3 = 0; i3 < 5; i3++)
                    {
                        for (int i4 = 0; i4 < 3; i4++)
                        {
                            ConsoleColor consoleColor = (i % 2 == 0 && i2 % 2 == 0 && i3 % 2 == 0 && i4 % 2 == 0) ? ConsoleColor.Green : ((i + i2 + i3 + i4) % 2 == 0 ? ConsoleColor.Yellow :  ConsoleColor.Red);

                            Console.ForegroundColor = consoleColor;

                            Console.WriteLine(
                                "[" + i + " Knights, " + i2 + " Bishops, " + i3 + " Rooks, " + i4 + " Queens] " + pieceTypeMatchups[i, i2, i3, i4] + " | " + pieceTypeMatchupsMoves[i, i2, i3, i4]
                                );
                        }
                    }
                }
            }

            // [4 Knights, 2 Bishops, 2 Rooks, 2 Queens] 473 | 3145

            File.WriteAllLines(PathManager.GetTXTPath("OTHER/TempFenList"), tempFENS);

            Console.WriteLine(cccount);

            return;
            NeuralNetwork neuNet = new NeuralNetwork(

                NeuronFunctions.ParametricReLU, NeuronFunctions.ParametricReLUDerivative, // STANDARD NEURON FUNCTION
                OUTPUT_SIGMOID, OUTPUT_SIGMOID_DERIVATIVE, // OUTPUT NEURON FUNCTION
                NeuronFunctions.SquaredDeviation, NeuronFunctions.SquareDeviationDerivative, // DEVIATION FUNCTION

                768, 16, 8, 7, 1);

            neuNet.GenerateRandomNetwork(new System.Random(), -1f, 1f, -1f, 1f);

            Console.WriteLine("Generated NN");

            TrainingData[] TrainingDataArr = RearrangeTrainingData(tTrainingData);
            TrainingData[][] TrainingDataBatches = TrainingData.CreateMiniBatches(TrainingDataArr, 40);

            int batchCount = TrainingDataBatches.Length;
            Console.WriteLine("BatchCount: " + batchCount);
            int curBatch = 0;
            int epoch = 0;

            Stopwatch sw = Stopwatch.StartNew();

            // ~((0.48/250)*8719)*4

            for (int i = 0; i < 1_500_000; i++)
            {
                neuNet.GradientDescent(TrainingDataBatches[curBatch], 1d, false);

                if (i % batchCount == 0)
                {
                    Console.WriteLine(sw.Elapsed);
                    LogManager.LogText("\n\n\n<<< EPOCH " + epoch + " >>> \n");
                    if (epoch++ % 3 == 0)
                    {
                        Console.WriteLine("EPOCH " + epoch + ": " + neuNet.CalculateDeviation(TrainingDataArr));
                    }
                    neuNet.RawLog();
                }
                if (++curBatch == batchCount) curBatch = 0;
            }

            Console.WriteLine(neuNet.CalculateDeviation(TrainingDataArr));

            //Console.WriteLine(sw.Elapsed);

            neuNet.RawLog();

            //nnv.InitNeuralNetworkVisualizor(neuNet.layer, new Vector2(-5f, -5f), new Vector2(5f, 5f), valueColorCodeGradient, selectInfoText, new GameObject[4] { knotPrefab, linePrefab, worldSpaceTextPrefab, worldSpaceCanvas });
            //neuNet.CalculateOutputs(new double[2] { 0.5d, 0.7d }, nnv);
            //UpdateAIOutputDots();
            //selectInfoText.text = "Deviation: " + neuNet.CalculateDeviation(trainingData);
        }

        public static int GetCPValFromTXTLineV2(string tLine)
        {
            int tL = tLine.Length, total = 0, y = 1;

            for (int i = tL; i-- > 0;)
            {
                switch (tLine[i])
                {
                    case '#':
                        if (total > 0) total = 9999;
                        else total = -9999;
                        break;
                    case '-':
                        total = -total;
                        break;
                    case '+':
                        break;

                    case ',':
                        return total;

                    default:

                        total += y * (tLine[i] - 48);
                        y *= 10;

                        break;
                }
            }

            return total;
        }

        public static int GetCPValFromTXTLine(string tLine)
        {
            int tL = tLine.Length, total = 0, y = 1;

            for (int i = tL; i-- > 0;)
            {
                switch (tLine[i])
                {
                    case '|':
                        return total;
                    case '-':
                        return -total;
                }

                total += y * (tLine[i] - 48);
                y *= 10;
            }

            return total;
        }

        public static ulong[] GetNNParamsFromFEN(string pFEN)
        {
            int tCharCount = pFEN.Length, tPos = 0;
            ulong[] uls = new ulong[12];
            for (int i = 0; i < tCharCount; i++)
            {
                int tChar = pFEN[i];
                if (tChar > 48 && tChar < 58)
                {
                    tPos += tChar - 48; // 49 ist die 1; 57 die 9
                    continue;
                }

                if (tChar == 32)
                {
                    uls[0] |= (((ulong)pFEN[++i] & 1) == 1) ? 127ul : 0ul;

                    break;
                }
                
                switch (tChar)
                {
                    case 47:
                        continue;


                    case 75: //K
                        uls[10] = ULONG_OPERATIONS.SetBitToOne(uls[10], tPos);
                        break;
                    case 107: //k
                        uls[11] = ULONG_OPERATIONS.SetBitToOne(uls[11], tPos);
                        break;
                    case 78: //N
                        uls[2] = ULONG_OPERATIONS.SetBitToOne(uls[2], tPos);
                        break;
                    case 110: //n
                        uls[3] = ULONG_OPERATIONS.SetBitToOne(uls[3], tPos);
                        break;
                    case 82: //R
                        uls[6] = ULONG_OPERATIONS.SetBitToOne(uls[6], tPos);
                        break;
                    case 114: //r
                        uls[7] = ULONG_OPERATIONS.SetBitToOne(uls[7], tPos);
                        break;
                    case 66: // B
                        uls[4] = ULONG_OPERATIONS.SetBitToOne(uls[4], tPos);
                        break;
                    case 98: //b
                        uls[5] = ULONG_OPERATIONS.SetBitToOne(uls[5], tPos);
                        break;
                    case 81: //Q
                        uls[8] = ULONG_OPERATIONS.SetBitToOne(uls[8], tPos);
                        break;
                    case 113: //q
                        uls[9] = ULONG_OPERATIONS.SetBitToOne(uls[9], tPos);
                        break;
                    case 80: //P
                        uls[0] = ULONG_OPERATIONS.SetBitToOne(uls[0], tPos);
                        break;
                    case 112: //p
                        uls[1] = ULONG_OPERATIONS.SetBitToOne(uls[1], tPos); //tPos + 56 - (tPos - tPos % 8)
                        break;
                }

                tPos++;
            }

            for (int i = 0; i < 12; i++)
            {
                uls[i] = ULONG_OPERATIONS.ReverseByteOrder(uls[i]);

                Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(uls[i]));
            }

            return uls;
        }

        public static double ConvertCPValsToNNSigmoid(int pInt)
        {
            return 1 / (Math.Exp(-1 / 160d * pInt) + 1);
        }

        public static double ConvertNNSigmoidtoCPVals(double pD)
        {
            return -160 * Math.Log(1 / pD - 1);
        }
        public static double ConvertNNSpecialSigmoidtoCPVals(double pD)
        {
            return -160 * Math.Log(1 / pD - 1) + .00004 * pD;
        }

        public static void PlayThroughDatabase(BoardManager pBM)
        {
            const int MAX_GAME_AMOUNT = 7900;

            pBM.LoadFenString(ENGINE_VALS.DEFAULT_FEN);
            pBM.SetJumpState();

            List<TrainingData> tTrainingData = new List<TrainingData>();

            //ulong[] ttULArr = new ulong[12];

            //Stopwatch sw = Stopwatch.StartNew();
            //
            //for (int i = 0; i < 1_000_000; i++)
            //{
            //    double[] tArr = NNUE_DB_DATA.ULONGARRS_TO_NNINPUTS(ttULArr);
            //}
            //
            //sw.Stop();

            //Console.WriteLine(sw.ElapsedMilliseconds);


            for (int e = 0; e < DATABASE_SIZE && e < MAX_GAME_AMOUNT; e++) {

                pBM.LoadJumpState();
                string pStr = DATABASE[e];
                int tL = pStr.Length;
                List<char> tchars = new List<char>();
                List<ulong[]> gameMoveInputs = new List<ulong[]>();

                for (int cpos = 1; cpos < tL; cpos++) {

                    char ch = pStr[cpos];
                    if (ch == ',')
                    {
                        int tMoveHash = NuCRe.GetNumber(new String(tchars.ToArray()));
                        List<Move> tMoves = new List<Move>();
                        pBM.GetLegalMoves(ref tMoves);
                        int ti_tL = tMoves.Count;
                        for (int m = 0; m < ti_tL; m++)
                        {
                            Move tM = tMoves[m];
                            if (tM.moveHash == tMoveHash)
                            {
                                pBM.PlainMakeMove(tM);

                                ulong[] tULArr = new ulong[12];

                                for (int i = 0; i < 64; i++)
                                {
                                    int tPT = pBM.pieceTypeArray[i];
                                    if (ULONG_OPERATIONS.IsBitOne(pBM.whitePieceBitboard, i)) tULArr[tPT - 1] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT - 1], i);
                                    else if (ULONG_OPERATIONS.IsBitOne(pBM.blackPieceBitboard, i)) tULArr[tPT + 5] = ULONG_OPERATIONS.SetBitToOne(tULArr[tPT + 5], i);
                                }

                                gameMoveInputs.Add(tULArr);


                                //for (int j = 0; j < 12; j++)
                                //    Console.WriteLine(ULONG_OPERATIONS.GetStringBoardVisualization(tULArr[j]));

                                break;
                            }
                        }

                        tchars.Clear();
                    }
                    else tchars.Add(ch);
                }

                NNUE_DB_DATA tNDD = new NNUE_DB_DATA(gameMoveInputs, Convert.ToInt32(new String(tchars.ToArray())));
                tTrainingData.AddRange(tNDD.TrainingData);
                if (e % 1000 == 0) Console.WriteLine(e + " Done!");
            }

            Console.WriteLine(tTrainingData.Count);



            NeuralNetwork neuNet = new NeuralNetwork(

                NeuronFunctions.ParametricReLU, NeuronFunctions.ParametricReLUDerivative, // STANDARD NEURON FUNCTION
                OUTPUT_SIGMOID, OUTPUT_SIGMOID_DERIVATIVE, // OUTPUT NEURON FUNCTION
                NeuronFunctions.SquaredDeviation, NeuronFunctions.SquareDeviationDerivative, // DEVIATION FUNCTION

                768, 16, 8, 7, 1);

            neuNet.GenerateRandomNetwork(new System.Random(), -1f, 1f, -1f, 1f);

            Console.WriteLine("Generated NN");

            TrainingData[] TrainingDataArr = RearrangeTrainingData(tTrainingData);
            TrainingData[][] TrainingDataBatches = TrainingData.CreateMiniBatches(TrainingDataArr, 40);

            int batchCount = TrainingDataBatches.Length;
            Console.WriteLine("BatchCount: " + batchCount);
            int curBatch = 0;
            int epoch = 0;

            Stopwatch sw = Stopwatch.StartNew();

            // ~((0.48/250)*8719)*4

            for (int i = 0; i < 1_500_000; i++)
            {
                neuNet.GradientDescent(TrainingDataBatches[curBatch], 1d, false);

                if (i % batchCount == 0)
                {
                    Console.WriteLine(sw.Elapsed);
                    LogManager.LogText("\n\n\n<<< EPOCH " + epoch + " >>> \n");
                    if (epoch++ % 3 == 0)
                    {
                        Console.WriteLine("EPOCH " + epoch + ": " + neuNet.CalculateDeviation(TrainingDataArr));
                    }
                    neuNet.RawLog();
                }
                if (++curBatch == batchCount) curBatch = 0; 
            }

            Console.WriteLine(neuNet.CalculateDeviation(TrainingDataArr));

            //Console.WriteLine(sw.Elapsed);

            neuNet.RawLog();

            //nnv.InitNeuralNetworkVisualizor(neuNet.layer, new Vector2(-5f, -5f), new Vector2(5f, 5f), valueColorCodeGradient, selectInfoText, new GameObject[4] { knotPrefab, linePrefab, worldSpaceTextPrefab, worldSpaceCanvas });
            //neuNet.CalculateOutputs(new double[2] { 0.5d, 0.7d }, nnv);
            //UpdateAIOutputDots();
            //selectInfoText.text = "Deviation: " + neuNet.CalculateDeviation(trainingData);
        }

        private static TrainingData[] RearrangeTrainingData(List<TrainingData> pNotRearrangedTrainingData)
        {
            Random rng = new Random();
            int tL = pNotRearrangedTrainingData.Count;
            TrainingData[] tRArr = new TrainingData[tL];
            for (int i = 0; i < tL; i++)
            {
                int tRand = rng.Next(0, tL - i);
                tRArr[i] = pNotRearrangedTrainingData[tRand];
                pNotRearrangedTrainingData.RemoveAt(tRand);
            }
            return tRArr;
        }

        private static double THI(double pVal)
        {
            if (pVal > 0) return Math.Log(pVal + 1);
            return -Math.Log(-pVal + 1);
        }

        private static double THI_DERIVATIVE(double pVal)
        {
            if (pVal < 0) return -1 / (pVal - 1);
            return 1 / (pVal + 1);
        }

        private static double OUTPUT_SPECIAL_SIGMOID(double pVal)
        {
            return .00004 * pVal + 1 / (Math.Exp(-1 / 160d * pVal) + 1);
        }

        private static double OUTPUT_SPECIAL_SIGMOID_DERIVATIVE(double pVal)
        {
            double d = Math.Exp(-.00625*pVal);
            return d / (160 * (2 * d + Math.Exp(-.0125 * pVal) + 1)) + .00004;
        }
         
        private static double OUTPUT_SIGMOID(double pVal)
        {
            return 2d / (Math.Exp(-pVal) + 1);
        }

        private static double OUTPUT_SIGMOID_DERIVATIVE(double pVal)
        {
            double d = Math.Exp(pVal);
            return 2 * d / Math.Pow(d + 1, 2);
        }

        #region | RUNTIME METHODS |

        public static (string, int) SearchForNextBookMoveV2(List<int> pMoveHashes)
        {
            int tL = pMoveHashes.Count;
            string tStr = tL == 0 ? "" : ";";
            for (int i = 0; i < tL; i++)
            {
                tStr += NuCRe.GetNuCRe(pMoveHashes[i]);
                if (i + 1 == tL) break;
                tStr += ",";
            }

            tL = tStr.Length;
            int resCount = 0;

            List<(string, int)> tResL = new List<(string, int)>();

            for (int i = 0; i < DATABASEV2_SIZE; i++)
            {
                string s = DATABASEV2[i];
                if (STRING_STARTS_WITH(s, tStr, tL))
                {
                    string sBook = GET_NEXT_BOOK_MOVE(s, tL);
                    resCount++;
                    ADD_BOOK_MOVE_TO_LIST(sBook, ref tResL);
                }
            }

            if (tResL.Count == 0) return ("", 0);

            SORT_RESULT_TUPLES(ref tResL, resCount, false);
            return GET_RANDOM_BOOK_MOVE(tResL);

            //return tResL[databaseRNG.Next(0, tResL.Count)];
        }

        public static (string, int) SearchForNextBookMove(string pNuCReMoveOpening)
        {
            int tL = pNuCReMoveOpening.Length, resCount = 0;

            List<(string, int)> tResL = new List<(string, int)>();

            for (int i = 0; i < DATABASE_SIZE; i++)
            {
                string s = DATABASE[i];
                if (STRING_STARTS_WITH(s, pNuCReMoveOpening, tL))
                {
                    string sBook = GET_NEXT_BOOK_MOVE(s, tL);
                    resCount++;
                    ADD_BOOK_MOVE_TO_LIST(sBook, ref tResL);
                }
            }

            SORT_RESULT_TUPLES(ref tResL, resCount, true);
            return GET_RANDOM_BOOK_MOVE(tResL);
        }

        public static (string, int) SearchForNextBookMove(List<int> pMoveHashes)
        {
            int tL = pMoveHashes.Count;
            string tStr = tL == 0 ? "" : ";";
            for (int i = 0; i < tL; i++)
            {
                tStr += NuCRe.GetNuCRe(pMoveHashes[i]);
                if (i + 1 == tL) break;
                tStr += ",";
            }
            return SearchForNextBookMove(tStr);
        }

        private static List<int> GetOpeningMatchesInDatabase(string pNuCReMoveOpening)
        {
            List<int> matches = new List<int>();
            int tL = pNuCReMoveOpening.Length;
            for (int i = 0; i < DATABASE_SIZE; i++)
            {
                string s = DATABASE[i];
                if (STRING_STARTS_WITH(s, pNuCReMoveOpening, tL)) matches.Add(i);
            }
            return matches;
        }

        private static (string, int) GET_RANDOM_BOOK_MOVE(List<(string, int)> pRefList)
        {
            int tL = pRefList.Count, c = 0;
            int[] tIArr = new int[tL];
            for (int i = 0; i < tL; i++)
            {
                c += pRefList[i].Item2;
                tIArr[i] = c;
            }

            int trand = databaseRNG.Next(0, c);

            for (int i = 0; i < tL; i++)
            {
                if (trand < tIArr[i])
                    return pRefList[i];
            }

            return ("", 0);
        }

        private static bool STRING_STARTS_WITH(string pStr, string pStrKey, int pKeyLen)
        {
            if (pKeyLen > pStr.Length) return false;
            for (int i = 0; i < pKeyLen; i++) if (pStr[i] != pStrKey[i]) return false;
            return true;
        }

        private static string GET_NEXT_BOOK_MOVE(string pStr, int keyLen)
        {
            int pStrLen = pStr.Length;
            string tM = "";
            while (++keyLen < pStrLen && pStr[keyLen] != ',') tM += pStr[keyLen];
            return tM;
        }

        private static void SORT_RESULT_TUPLES(ref List<(string, int)> pRefList, double pTotal, bool pSortOut)
        {
            int tL = pRefList.Count;
            for (int i = 0; i < tL; i++)
            {
                for (int j = 0; j < tL - 1; j++)
                {
                    if (pRefList[j].Item2 > pRefList[j + 1].Item2) continue;
                    (string, int) temp = pRefList[j];
                    pRefList[j] = pRefList[j + 1];
                    pRefList[j + 1] = temp;
                }
            }
            if (!pSortOut) return;
            for (int i = 0; i < tL; i++)
            {
                int c = pRefList[i].Item2;
                if (c < minMovesInDatabaseForMatch || c / pTotal < minPercentage)
                {
                    for (int j = i; j < tL; j++)
                        pRefList.RemoveAt(i);
                    break;
                }
            }
        }

        private static void ADD_BOOK_MOVE_TO_LIST(string pBookMoveStr, ref List<(string, int)> pRefList)
        {
            int tL = pRefList.Count;
            for (int i = 0; i < tL; i++)
            {
                (string, int) tEl = pRefList[i];
                if (tEl.Item1 == pBookMoveStr)
                {
                    pRefList[i] = (pBookMoveStr, tEl.Item2 + 1);
                    return;
                }
            }
            pRefList.Add((pBookMoveStr, 1));
        }

        #endregion

        #region | CREATION |

        private static int tOCount = 0;
        private static List<string> optimizedSizeDatabaseStrings = new List<string>();

        public static void OptimizeSizeOfDatabase()
        {
            RecursiveOpeningBookSearch(0, "", DATABASE.ToList(), new List<int>());
            Console.WriteLine("Optmized Line Count: " + tOCount);
            File.WriteAllLines(PathManager.GetTXTPath("DATABASES/TLM_DB2"), optimizedSizeDatabaseStrings);
        }

        private static void RecursiveOpeningBookSearch(int pDepth, string pStr, List<string> pSearchL, List<int> pPlayCounts)
        {
            int tL = pStr.Length, resCount = 0, tSLC = pSearchL.Count;

            List<(string, int)> tResL = new List<(string, int)>();
            List<string> newEntryList = new List<string>();

            for (int i = 0; i < tSLC; i++)
            {
                string s = pSearchL[i];
                if (STRING_STARTS_WITH(s, pStr, tL))
                {
                    newEntryList.Add(s);
                    string sBook = GET_NEXT_BOOK_MOVE(s, tL);
                    resCount++;
                    ADD_BOOK_MOVE_TO_LIST(sBook, ref tResL);
                }
            }

            SORT_RESULT_TUPLES(ref tResL, resCount, true);

            if (tResL.Count == 0)
            {
                //string[] tStrs = pStr.Substring(1, tL - 1).Split(',');
                //int t_ti_L = pPlayCounts.Count;
                //List<string> tStringCompL = new List<string>();
                //for (int i = 0; i < t_ti_L; i++)
                //    tStringCompL.Add(tStrs[i] + "," + NuCRe.GetNuCRe(pPlayCounts[i]) + ",");
                //optimizedSizeDatabaseStrings.Add(String.Concat(tStringCompL));
                optimizedSizeDatabaseStrings.Add(pStr);
                tOCount++;
                return;
            }

            int tResLC = tResL.Count;
            for (int j = 0; j < tResLC; j++) {
                pPlayCounts.Add(tResL[j].Item2);
                RecursiveOpeningBookSearch(pDepth + 1, pStr + (pDepth == 0 ? ";" : ",") + tResL[j].Item1, newEntryList, pPlayCounts);
                pPlayCounts.RemoveAt(pDepth);
            }
        }

        public static void ProcessCreatedDatabase()
        {
            completedThreads = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();

            pieceTypeDict.Add('a', 1);
            pieceTypeDict.Add('b', 1);
            pieceTypeDict.Add('c', 1);
            pieceTypeDict.Add('d', 1);
            pieceTypeDict.Add('e', 1);
            pieceTypeDict.Add('f', 1);
            pieceTypeDict.Add('g', 1);
            pieceTypeDict.Add('h', 1);
            pieceTypeDict.Add('N', 2);
            pieceTypeDict.Add('B', 3);
            pieceTypeDict.Add('R', 4);
            pieceTypeDict.Add('Q', 5);
            pieceTypeDict.Add('K', 6);
            pieceTypeDict.Add('O', 6);

            string tPath = PathManager.GetTXTPath("DATABASES/OpeningDatabaseV1");
            string[] strs = File.ReadAllLines(tPath);
            int tL = strs.Length;
            skippedChars['!'] = skippedChars['?'] = skippedChars['+'] = skippedChars['#'] = skippedChars[' '] = true;

            for (int i = 0; i < ENGINE_VALS.CPU_CORES; i++)
            {
                bM = BOT_MAIN.boardManagers[i];
                bM.LoadFenString("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
                bM.SetJumpState();
            }

            int amountPerThread = tL / ENGINE_VALS.CPU_CORES, tempP = 0;

            for (int i = 0; i < ENGINE_VALS.CPU_CORES - 1; i++)
            {
                TLMDatabaseThreadObject2 tbto2_ts = new TLMDatabaseThreadObject2(strs, tempP, tempP += amountPerThread, BOT_MAIN.boardManagers[i], skippedChars, rankArr, pieceTypeDict);
                ThreadPool.QueueUserWorkItem(new WaitCallback(tbto2_ts.DatabaseThreadMethod));
            }

            TLMDatabaseThreadObject2 tbto2 = new TLMDatabaseThreadObject2(strs, tempP, tL, BOT_MAIN.boardManagers[ENGINE_VALS.CPU_CORES - 1], skippedChars, rankArr, pieceTypeDict);
            ThreadPool.QueueUserWorkItem(new WaitCallback(tbto2.DatabaseThreadMethod));

            while (completedThreads != ENGINE_VALS.CPU_CORES) Thread.Sleep(100);

            stopwatch.Stop();

            Console.WriteLine(stopwatch.ElapsedMilliseconds);

            string tPath2 = Path.GetFullPath("TLM_DB1.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
            File.WriteAllLines(tPath2, processedPGNs);
        }

        public static void CreateDatabase()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();


            ReadChunks(lichessDatabasePath);


            stopwatch.Stop();

            int g = 0;
            foreach (TLMDatabaseThreadObject task in threadTasks)
            {
                g += task.lookedThroughGames;
                validPGNs.AddRange(task.localValidPGNs);
            }

            Console.WriteLine("Games under restrictions found: " + validPGNs.Count + " / " + g);
            Console.WriteLine(stopwatch.ElapsedMilliseconds);
            Console.WriteLine(validPGNs[validPGNs.Count - 1]);

            string tPath = Path.GetFullPath("OpeningDatabaseV1.txt").Replace(@"\\bin\\Debug\\net6.0", "").Replace(@"\bin\Debug\net6.0", "");
            File.WriteAllLines(tPath, validPGNs);
        }

        private static void ReadChunks(string path)
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            {
                long lastGameBeginByteOffset = 0;

                int tasksStarted = 0;
                long fsLen = fs.Length;

                while (lastGameBeginByteOffset < fsLen)
                {
                    fs.Position = lastGameBeginByteOffset;
                    byte[] tBytes = new byte[chunkSize];
                    int bCount = fs.Read(tBytes, 0, chunkSize);
                    TLMDatabaseThreadObject tTDTO = new TLMDatabaseThreadObject(tBytes);
                    threadTasks.Add(tTDTO);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(tTDTO.DatabaseThreadMethod));
                    tasksStarted++;
                    lastGameBeginByteOffset += chunkSize + threadMargin;
                }

                while (completedThreads != tasksStarted) Thread.Sleep(100);

                Console.WriteLine("ByteCount = " + fsLen);
                lastGameBeginByteOffset = 0;

                int tttL = threadTasks.Count - 1;

                for (int i = 0; i < tttL; i++)
                {
                    int tLen = threadMargin - threadTasks[i].lastGameByteOffset + chunkSize;
                    lastGameBeginByteOffset += chunkSize + threadMargin - tLen;
                    fs.Position = lastGameBeginByteOffset;
                    byte[] tBytes = new byte[tLen];
                    int bCount = fs.Read(tBytes, 0, tLen);
                    TLMDatabaseThreadObject tTDTO = new TLMDatabaseThreadObject(tBytes);
                    threadTasks.Add(tTDTO);
                    ThreadPool.QueueUserWorkItem(new WaitCallback(tTDTO.DatabaseThreadMethod));
                    tasksStarted++;
                    lastGameBeginByteOffset += tLen;
                }

                while (completedThreads != tasksStarted) Thread.Sleep(100);
            }
        }

        #endregion
    }

    #region | THREAD OBJECTS |

    public class TLMDatabaseThreadObject
    {
        public int lastGameByteOffset = -1, lookedThroughGames = 0;

        private byte[] fileChunk;
        private int minElo, fileChunkSize;

        public List<string> localValidPGNs = new List<string>();

        public TLMDatabaseThreadObject(byte[] pArr) 
        {
            fileChunk = (byte[])pArr.Clone();
            fileChunkSize = pArr.Length;
            minElo = TLMDatabase.minElo;
        }

        private static int STRING_TO_INT(string s)
        {
            int total = 0, y = 1;
            for (int x = s.Length - 1; x > 0; x--)
            {
                total += (y * (s[x] - '0'));
                y *= 10;
            }
            if (s[0] == '-') total *= -1;
            else total += (y * (s[0] - '0'));
            return total;
        }

        public void DatabaseThreadMethod(object obj)
        {
            bool curGameValid = true;
            int tEloC = 0;
            const char nextLine = '\n';
            for (int i = 0; i < fileChunkSize; i++)
            {
                char tC = (char)fileChunk[i];
                if (nextLine == tC)
                {
                    if (i == fileChunkSize - 1) break;
                    switch ((char)fileChunk[++i])
                    {
                        case '[':
                            if (i + 9 < fileChunkSize && fileChunk[i + 5] == 'e' && fileChunk[i + 6] == 'E' && fileChunk[i + 7] == 'l' && fileChunk[i + 8] == 'o' && fileChunk[i + 9] == ' ')
                            {
                                string tempStr = "";
                                char t_ts_ch;
                                while (++i < fileChunkSize && nextLine != (t_ts_ch = (char)fileChunk[i])) tempStr += t_ts_ch;
                                if (i >= fileChunkSize) break;
                                int tcp = 9;
                                string tElo = "";
                                while ((t_ts_ch = tempStr[++tcp]) != '"') tElo += t_ts_ch;
                                tEloC++;
                                if (STRING_TO_INT(tElo) < minElo) curGameValid = false;
                                i++;

                                string tempStr3 = "";
                                char t_ts_ch3;
                                while (++i < fileChunkSize && nextLine != (t_ts_ch3 = (char)fileChunk[i])) tempStr3 += t_ts_ch3;
                                if (i >= fileChunkSize) break;
                                tcp = 9;
                                tElo = "";
                                while ((t_ts_ch3 = tempStr3[++tcp]) != '"') tElo += t_ts_ch3;
                                tEloC++;

                                if (STRING_TO_INT(tElo) < minElo) curGameValid = false;
                                i += 70;
                            }

                            break;
                        case '\n':

                            if (tEloC == 2)
                            {
                                char t_ts_ch2;
                                List<char> tchl = new List<char>();
                                while (++i < fileChunkSize && nextLine != (t_ts_ch2 = (char)fileChunk[i])) tchl.Add(t_ts_ch2);
                                if (i >= fileChunkSize) break;
                                if (curGameValid) localValidPGNs.Add(new string(tchl.ToArray()));
                                curGameValid = true;
                                lastGameByteOffset = i;
                                lookedThroughGames++;
                                tEloC = 0;
                            }
                            break;
                    }
                }
            }
            TLMDatabase.completedThreads++;
            TLMDatabase.games += lookedThroughGames;

            fileChunk = Array.Empty<byte>();
        }
    }

    public class TLMDatabaseThreadObject2
    {
        private string[] loadedUnprocessedStrs;
        private bool[] skippedChars;
        private int from, to;
        private IBoardManager bM;
        private int minMoves;
        private Dictionary<char, int> pieceTypeDict;
        private int[] rankArr;
        private List<string> tempStringListHolder = new List<string>();

        public TLMDatabaseThreadObject2(string[] pStrs, int pFrom, int pTo, IBoardManager pBM, bool[] pSkippedChars, int[] pRankArray, Dictionary<char, int> pPieceTypeDict)
        {
            loadedUnprocessedStrs = pStrs;
            from = pFrom;
            to = pTo;
            bM = pBM;
            skippedChars = pSkippedChars;
            minMoves = TLMDatabase.minMoves;
            rankArr = pRankArray;
            pieceTypeDict = pPieceTypeDict;
        }

        public void DatabaseThreadMethod(object obj)
        {
            for (int i = from; i < to; i++)
            {
                ProcessDatabaseV1Line(loadedUnprocessedStrs[i]);
            }

            TLMDatabase.processedPGNs.AddRange(tempStringListHolder);
            TLMDatabase.completedThreads++;
        }

        public void ProcessDatabaseV1Line(string pStr)
        {
            bM.LoadJumpState();

            int tL = pStr.Length, tMoveC = 0;
            List<char> tempL = new List<char>();
            bool valsOpened = false, legitGame = false;
            for (int i = 0; i < tL; i++)
            {
                char c = pStr[i];

                if (c == '.')
                {
                    legitGame = true;
                    int tI;
                    while (tempL.Count > 0 && Char.IsDigit(tempL[tI = tempL.Count - 1]))
                        tempL.RemoveAt(tI);
                    continue;
                }

                if (valsOpened && '}' == c)
                {
                    valsOpened = false;
                }
                else if ('{' == c)
                {
                    valsOpened = true;
                    tMoveC++;
                    tempL.Add(',');
                }
                else if (!valsOpened && !skippedChars[c]) tempL.Add(c);
            }

            if (!legitGame) return;

            string[] tspl = new string(tempL.ToArray()).Split(',');

            if (tMoveC < minMoves) return;

            List<char> newGameString = new List<char>() { ';' };

            for (int i = 0; i < tMoveC; i++)
            {
                List<Move> tMoves = new List<Move>();
                bM.GetLegalMoves(ref tMoves);

                string curSpl = tspl[i];
                int tPT = pieceTypeDict[curSpl[0]];

                if (tPT != 1) curSpl = curSpl.Remove(0, 1);

                int startLine = -1, startPos = -1, endPos = -1, promType = -1;
                bool isCapture = false, isRochade = false, isPromotion = false;

                if (curSpl[curSpl.Length - 2] == '=')  // Promotion (Könnte auch Promotion Capture sein)
                {
                    isPromotion = true;
                    promType = pieceTypeDict[curSpl[curSpl.Length - 1]];

                    if (curSpl.Length == 4) // Normale Promotion
                    {
                        endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[0], curSpl[1] }));
                    }
                    else // Capture Promotion
                    {
                        isCapture = true;
                        string[] t_ts_spl = curSpl.Split('x');

                        if (t_ts_spl[0].Length == 1) startLine = t_ts_spl[0][0] - 97;

                        endPos = SQUARES.NumberNotation(t_ts_spl[1]);
                    }

                }
                else if (curSpl.Contains('x'))  // Captures
                {
                    isCapture = true;
                    string[] t_ts_spl = curSpl.Split('x');

                    switch (t_ts_spl[0].Length)
                    {
                        case 1: // Line or Rank Specified
                            startLine = t_ts_spl[0][0] - 97;
                            break;

                        case 2: // Square Specified
                            startPos = SQUARES.NumberNotation(t_ts_spl[0]);
                            break;
                    }

                    endPos = SQUARES.NumberNotation(t_ts_spl[1]);
                }
                else if (curSpl[1] == 'O')
                {
                    isRochade = true;
                    if (curSpl.Length == 2) // Kingside Rochade
                        endPos = 6;
                    else // Queenside Rochade
                        endPos = 2;
                }
                else // Normaler Zug
                {
                    switch (curSpl.Length)
                    {
                        case 2: // Nothing extra specified
                            endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[0], curSpl[1] }));
                            break;
                        case 3: // Line or Rank Specified
                            startLine = curSpl[0] - 97;
                            endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[1], curSpl[2] }));
                            break;

                        case 4: // Square Specified
                            startPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[0], curSpl[1] }));
                            endPos = SQUARES.NumberNotation(new string(new char[2] { curSpl[2], curSpl[3] }));
                            break;
                    }
                }

                int molc = tMoves.Count;
                Move? theMove = null;

                bool b = startPos == -1 && startLine == -1, rankAndNotFileSpecified;

                if (rankAndNotFileSpecified = startLine < -1) startLine += 48;

                for (int m = 0; m < molc; m++)
                {
                    Move curMove = tMoves[m];
                    if (curMove.pieceType == tPT && curMove.isRochade == isRochade && curMove.isPromotion == isPromotion && curMove.isCapture == isCapture)
                    {
                        if (isRochade)
                        {
                            if (endPos == curMove.endPos % 8)
                            {
                                theMove = curMove;
                                break;
                            }
                        }
                        else if (endPos == curMove.endPos)
                        {
                            if (isPromotion)
                            {
                                if (promType == curMove.promotionType)
                                {
                                    if (startLine == -1 || startLine == curMove.startPos % 8)
                                    {
                                        theMove = curMove;
                                        break;
                                    }
                                }
                            }
                            else if (b)
                            {
                                theMove = curMove;
                                break;
                            }
                            else if (rankAndNotFileSpecified)
                            {
                                if (startLine == rankArr[curMove.startPos])
                                {
                                    theMove = curMove;
                                    break;
                                }
                            }
                            else if (startPos == curMove.startPos)
                            {
                                theMove = curMove;
                                break;
                            }
                            else if (startLine == curMove.startPos % 8)
                            {
                                theMove = curMove;
                                break;
                            }
                        }
                    }
                }

                if (theMove == null)
                {
                    Console.WriteLine("NULL REFERENCE; HIER IST NOCH IWO N ERROR");
                    return;
                }

                string tS = NuCRe.GetNuCRe(theMove.moveHash);
                for (int m = 0; m < tS.Length; m++)
                    newGameString.Add(tS[m]);

                bM.PlainMakeMove(theMove);

                newGameString.Add(',');
            }

            string result = tspl[tMoveC];
            char resCH = '2';

            if (result.Length > 4) resCH = '1';
            else if (result[0] == '0') resCH = '0';

            newGameString.Add(resCH);

            tempStringListHolder.Add(new string(newGameString.ToArray()));
        }
    }

    #endregion

    public class FullyLoadedGame
    {
        public List<Move> Moves = new List<Move>();
        public int Outcome;

        public FullyLoadedGame(List<Move> pMoves, int pOutcome)
        {
            Moves = pMoves;
            Outcome = pOutcome;
        }
    }

    public class NNUE_DB_DATA
    {
        public List<ulong[]> Inputs = new List<ulong[]>();
        public int Outcome;

        public List<TrainingData> TrainingData = new List<TrainingData>();

        public NNUE_DB_DATA(List<ulong[]> pInputs, int pOutcome)
        {
            Inputs = pInputs;
            Outcome = pOutcome;

            int tL = pInputs.Count;
            for (int i = 0; i < tL; i++)
            {
                if (i < 5 || i > tL - 6) continue;
                TrainingData.Add(
                    new TrainingData(
                        ULONGARRS_TO_NNINPUTS(pInputs[i]),
                        new double[1] { Outcome }
                        //pInputs[i]
                    )
                );
            }
        }

        public static double[] ULONGARRS_TO_NNINPUTS(ulong[] pULArrs)
        {
            double[] rArr = new double[768];
            int a = 0;
            for (int j = 0; j < 12; j++)
                for (int i = 0; i < 64; i++)
                    rArr[a++] = (pULArrs[j] >> i) & 1;
            return rArr;
        }
    }
}
