using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Transactions;

namespace Miluva
{
    #pragma warning disable CA1416

    public static class CAM_SETTINGS
    {
        // Diese Werte können modifiziert werden; zur Erklärung alles einmal auf Deutsch:


        //**************************************************************************************************************************************************
        //**************************************************************************************************************************************************
        //**************************************************************************************************************************************************



        // LAB-Pythagoras-Threshold, der zur Outline Erkennung der Felder / Figuren genutzt wird
        public const double COLOR_DIFFERENCE_THRESHOLD = 14.5;
        public const double COLOR_DIFFERENCE_THRESHOLD_FOR_PIECE_RECOGNITION = 4.5;

        // Eine Linie darf kein dickere Pixelbreite als diese haben
        public const int MAX_LINE_WIDTH = 4;

        // Prezentsatz der gefüllten Pixel, damit eine horizontale / vertikale Linie als sicher relevant anerkannt werden kann
        public const double HORIZONTALLINE_FULL_SECURE_DETERMINANT = .28;
        public const double VERTICALLINE_FULL_SECURE_DETERMINANT = .1;

        // Mindestabstand zwischen den einzelnen relevanten Linien; in diesem Intervall entscheidet sich der Algorithmus für die prozentual stärkste Linie
        public const int HORIZONTALLINE_MIN_DIST = 20;

        // Zwei Horizontale Linien dürfen nicht mehr als dieser Multiplikator des Abstands zur vorherigen Linie auseinander liegen 
        public const double MAX_HORIZONTALLINE_DIST_INCR = 1.5;

        // Vom oberen Rand der Aufnahme ausgehend darf keine Linie diese Distanz zum Rand des Bildes unterschreiten
        public const int VERTICALLINE_MIN_PIC_END_DIST = 10; //50

        // Maximale Abweichung in Pixeln, die der Abstand zur nächsten Linie im Vergleich zur vorherigen Linie haben darf
        public const int VERTICALLINE_MAX_DEVIATION_FROM_AVERAGE = 9;
        public const int HORIZONTALLINE_MAX_DEVIATION_FROM_AVERAGE_DOWN = 7;
        public const int HORIZONTALLINE_MAX_DEVIATION_FROM_AVERAGE_UP = 7;

        // Bei Verfeinerung der Posierung der horizontalen Linien, dürfen nur Pixel mit diesem Y-Abstand als Wertpunkt für die lineare Regression genutzt werden
        public const int HORIZONTALLINE_MAX_LINREG_OPTIMIZATION_DIST = 16;

        // Zur Verfeinerung dieser Posierung müssen aber mindestens diese Anzahl an Wertpunkten gefunden werden
        public const int HORIZONTALLINE_MIN_LINREG_OPTIMIZATION_POINTS = 55;

        public const int HORIZONTALLINE_INTEGRAL_SIZE = 14;
        public const int HORIZONTALLINE_INTEGRAL_MIN_DIST = 14;

        // Der mindeste Prozentsatz an benötigten ausgefüllten Pixeln, damit ein gefülltes Objekt als Feld anerkannt werden kann
        public const double SQUARYNESS_DETERMINANT = .47;

        // Minimale Größe (Summe aller Pixel) für solch ein Objekt
        public const double MIN_FILL_OBJECT_SIZE = 1300;

        // Maximales Aspect Ratio für [...]
        public const double SQUARE_MAX_ASPECT_RATIO_DISTORTION = 1.4;

        // Maximale Summe / Höchster Wert der vier Skalarprodukte der Objekte für [...]
        public const double SQUARE_MAX_SUM_OF_SCALARPRODUCT = 0.35;
        public const double SQUARE_MAX_INDIVIDUAL_SCALARPRODUCT = 0.28;

        // Sobald eine an ein als Square anerkanntes Objekt angrenzende Linie gefunden wurde; wird sie mit folgedem zusätzlichem addierten Prozentsatz gewichtet
        public const double SECURE_SQUARE_LINE_DETERMINATION_BOOST = 0.2;
        public const double SECURE_SQUARE_LINE_DETERMINATION_BOOST_ONLY_HORIZONTAL = 0.01;

        // Nach der Erstellung des Gitters der Felder; Maximale Anzahl an Randpixeln dieser Felder
        public const int SQUARE_MAX_EDGE_PIXEL = 0;

        // Maximale Grö0e eines Objekts zur Erkennung einer Störung (einzelne Noise Partikel zum Beispiel)
        public const int MAX_DISTORTION_SIZE = 30;

        // Sollte die "Main Area", also das Spielbrett mit der Pixelmasse oder mit dem Rechteckflächeninhalt erkannt werden
        public const bool MAIN_AREA_SELECTION_WITH_PIXELCOUNT = true;

        // Zusätze zur Main Area benötigen mindestens diese Größe (erneut Pixelmasse)
        public const int MAIN_AREA_ADDITION_MIN_SIZE = 70000000;

        // Quadrierter maximaler Abstand, den ein Pixel zu einem anderen der Main Area haben muss
        public const double MAIN_AREA_ADDITION_DIST_THRESHOLD = 100;

        // Threshold zur Erkennung der Figuren
        public const double MIN_PIECE_RECOGNITION_DETERMINANT = 12;

        // Anzahl an möglichen Lösungen, die der Algorithmus als möglich ansehen soll
        public const int PIECE_RECOGNITION_SOLUTIONS = 6;

        // Der durchschnittliche Abstand vom Zentrum zu den Ecken eines Feldes mal diesen Wert ergibt den Radius der zu scannenden Feld-Fläche
        public const double PIECE_RECOGNITION_FIELD_SIZE_RADIUS_MULT = .66;

        // Größe des Bilds auf die das Programm den Input skaliert
        public const int PIC_RESOLUTION_X = 800, PIC_RESOLUTION_Y = 450;

        // Perspektivisch Zentrale Position -> zur Evaluierung von allgemein "durchschnittlichen" Feldern
        public const int SQUARE_POSX_PREFERATION = 400, SQUARE_POSY_PREFERATION = 320;



        //**************************************************************************************************************************************************
        //**************************************************************************************************************************************************
        //**************************************************************************************************************************************************


        public static string PROJECT_DIRECTORY = "";
        public const double SQUARE_MIN_ASPECT_RATIO_DISTORTION = 1 / SQUARE_MAX_ASPECT_RATIO_DISTORTION;
    }
    public class LAB_CREATION_THREAD_OBJ
    {
        public int FROM = 0, TO = 0;

        public LAB_CREATION_THREAD_OBJ(int pFrom, int pTo)
        {
            FROM = pFrom;
            TO = pTo;
        }

        public void TMethod(object? obj)
        {
            for (int r = FROM; r < TO; r++)
                for (int g = 0; g < 256; g++)
                    for (int b = 0; b < 256; b++)
                        STATIC_MAIN_CAMERA_ANALYSER.labDictionary[(r << 16) | (g << 8) | b] = STATIC_MAIN_CAMERA_ANALYSER.GetLABFromRGB(new STATIC_MAIN_CAMERA_ANALYSER.COLOR_VALUES(r, g, b));

            STATIC_MAIN_CAMERA_ANALYSER.finishedThreads++;
        }
    }

    public static class STATIC_MAIN_CAMERA_ANALYSER
    {
        public struct COLOR_VALUES
        {
            public double first, second, third;

            public COLOR_VALUES(double f, double s, double t)
            {
                first = f;
                second = s;
                third = t;
            }

            public override string ToString()
            {
                return first + "," + second + "," + third;
            }

            public static double operator ^(COLOR_VALUES c1, COLOR_VALUES c2)
            {
                double tl = (c1.first - c2.first), ta = (c1.second - c2.second), tb = (c1.third - c2.third);
                return Math.Sqrt(tl * tl + ta * ta + tb * tb);
            }
        }

        public static COLOR_VALUES[] labDictionary = new COLOR_VALUES[16777216];

        private static double differenceThreshold = CAM_SETTINGS.COLOR_DIFFERENCE_THRESHOLD * CAM_SETTINGS.COLOR_DIFFERENCE_THRESHOLD;
        private static double differenceThreshold2 = CAM_SETTINGS.COLOR_DIFFERENCE_THRESHOLD_FOR_PIECE_RECOGNITION * CAM_SETTINGS.COLOR_DIFFERENCE_THRESHOLD_FOR_PIECE_RECOGNITION;

        public static int finishedThreads = 0;

        public static List<ulong> RESULT = new List<ulong>();

        public static void SETUP()
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            CAM_SETTINGS.PROJECT_DIRECTORY = Environment.CurrentDirectory.Replace(@"\bin\Debug\net6.0", "") + @"\CAMERA_ANALYSIS";

            for (int i = 0; i < 32; i++)
            {
                ThreadPool.QueueUserWorkItem(
                    new WaitCallback(new LAB_CREATION_THREAD_OBJ(i * 8, i * 8 + 8).TMethod)
                );
            }

            while (finishedThreads < 32) Thread.Sleep(50);

            Console.WriteLine("LAB Dictionary created in " + stopwatch.ElapsedMilliseconds + "ms");
            stopwatch.Stop();
        }

        public static List<ulong> ANALYSE()
        {
            RESULT.Clear();
            System.Drawing.Image? resizedImg = null;
            Image? timg = null;
            PNG_EXTRACTOR? pe = null;
            try
            {

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                _ = new PYTHONHANDLER("VIDCAP");

                Console.WriteLine("Python Image Input captured in " + stopwatch.ElapsedMilliseconds + "ms");
                stopwatch.Restart();

                timg = Image.FromFile(CAM_SETTINGS.PROJECT_DIRECTORY + @"\cam.png");

                //File.Delete(CAM_SETTINGS.PROJECT_DIRECTORY + @"\ResizedImage.jpg");

                resizedImg = ResizeImage(timg, new Size(CAM_SETTINGS.PIC_RESOLUTION_X, CAM_SETTINGS.PIC_RESOLUTION_Y));
                resizedImg.Save(CAM_SETTINGS.PROJECT_DIRECTORY + @"\ResizedImage.jpg", ImageFormat.Jpeg);

                timg.Dispose();

                Console.WriteLine("STEP 0 - Image Resizing   [ " + stopwatch.ElapsedMilliseconds + "ms ]");

                pe = new PNG_EXTRACTOR(

                    CAM_SETTINGS.PROJECT_DIRECTORY + @"\ResizedImage.jpg",

                    new string[16] {
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step1.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step2.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step3.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step4.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step5.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step6.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step7.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step8.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step9.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step10.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step11.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step12.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step13.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step14.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step15.png",
                CAM_SETTINGS.PROJECT_DIRECTORY + @"\Steps\Step16.png"
                    }

                );

                resizedImg.Dispose();

                Console.WriteLine("Analysis finished in " + stopwatch.ElapsedMilliseconds + "ms");

           }
           catch (Exception e)
           {
               Console.WriteLine(e.ToString());
               resizedImg?.Dispose();
               timg?.Dispose();
               pe?.img?.Dispose();
           }

            return RESULT;
        }

        public static bool ColorDifference(int c1, int c2)
        {
            COLOR_VALUES cv1 = labDictionary[c1], cv2 = labDictionary[c2];
            double tl = (cv1.first - cv2.first), ta = (cv1.second - cv2.second), tb = (cv1.third - cv2.third);
            return (tl * tl + ta * ta + tb * tb) > differenceThreshold;
        }

        public static bool ColorDifference2(int c1, int c2)
        {
            COLOR_VALUES cv1 = labDictionary[c1], cv2 = labDictionary[c2];
            double tl = (cv1.first - cv2.first), ta = (cv1.second - cv2.second), tb = (cv1.third - cv2.third);
            return (tl * tl + ta * ta + tb * tb) > differenceThreshold2;
        }

        public static double GetActualColorDifferenceVal(int c1, int c2)
        {
            COLOR_VALUES cv1 = labDictionary[c1], cv2 = labDictionary[c2];
            double tl = (cv1.first - cv2.first), ta = (cv1.second - cv2.second), tb = (cv1.third - cv2.third);
            return tl * tl + ta * ta + tb * tb;
        }

        public static COLOR_VALUES GetLABFromRGB(COLOR_VALUES rgb)
        {
            COLOR_VALUES XYZBase = GetXYZFromRGB(new COLOR_VALUES(rgb.first, rgb.second, rgb.third));

            double lLAB = 116 * LABFunction(XYZBase.second / 1.0000001) - 16;
            double aLAB = 500 * (LABFunction(XYZBase.first / 0.95047) - LABFunction(XYZBase.second / 1.0000001));
            double bLAB = 200 * (LABFunction(XYZBase.second / 1.0000001) - LABFunction(XYZBase.third / 1.08883));

            return new COLOR_VALUES(lLAB, aLAB, bLAB);
        }

        private static COLOR_VALUES GetXYZFromRGB(COLOR_VALUES rgb)
        {
            double sRGBRed = GammaCorrection((double)rgb.first / 255.0);
            double sRGBGreen = GammaCorrection((double)rgb.second / 255.0);
            double sRGBBlue = GammaCorrection((double)rgb.third / 255.0);

            double xXYZ = sRGBRed * 0.4124564 + sRGBGreen * 0.3575761 + sRGBBlue * 0.1804375;
            double yXYZ = sRGBRed * 0.2126729 + sRGBGreen * 0.7151522 + sRGBBlue * 0.0721750;
            double zXYZ = sRGBRed * 0.0193339 + sRGBGreen * 0.1191920 + sRGBBlue * 0.9503041;

            return new COLOR_VALUES(xXYZ, yXYZ, zXYZ);
        }

        private static double LABFunction(double val)
        {
            if (val > 0.008856) return Math.Pow(val, 1.0 / 3.0);
            return 7.787 * val + 16.0 / 116.0;
        }

        private static double GammaCorrection(double val)
        {
            if (val > .04045) return Math.Pow((val + .055) / 1.055, 2.4);
            return val / 12.92;
        }

        // Diese Funktion gibt es im Internet: https://www.c-sharpcorner.com/UploadFile/ishbandhu2009/resize-an-image-in-C-Sharp/
        private static System.Drawing.Image ResizeImage(System.Drawing.Image imgToResize, Size size)
        {
            int sourceWidth = imgToResize.Width, sourceHeight = imgToResize.Height;
            float nPercent, nPercentW, nPercentH;
            nPercentW = (float)size.Width / sourceWidth;
            nPercentH = (float)size.Height / sourceHeight;
            nPercent = Math.Min(nPercentW, nPercentH);
            int destWidth = (int)(sourceWidth * nPercent), destHeight = (int)(sourceHeight * nPercent);
            Bitmap b = new Bitmap(destWidth, destHeight);
            Graphics g = Graphics.FromImage(b);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(imgToResize, 0, 0, destWidth, destHeight);
            g.Dispose();
            return b;
        }
    }

    public class PNG_EXTRACTOR
    {
        private const byte edgeColorByte = 0b11111111, bgColorByte = 0b00000000;
        private bool[] activatedPixels, lookedThroughPixels;
        private int[] pixelRowVals, pixelColumnVals;
        private int[] pixelToLeftVals, pixelToRightVals;
        private int width, height, pixelCount;
        private bool[] horsqstartpos = new bool[CAM_SETTINGS.PIC_RESOLUTION_Y];
        private bool[] versqstartpos = new bool[CAM_SETTINGS.PIC_RESOLUTION_X];
        private double[] horsqstartposd = new double[CAM_SETTINGS.PIC_RESOLUTION_Y];
        private double[] versqstartposd = new double[CAM_SETTINGS.PIC_RESOLUTION_X];
        private List<int>[] finalVertLinePoses = new List<int>[9];
        private List<int>[] finalHorLinePoses = new List<int>[9];
        private List<int> recursiveLimits = new List<int>();
        public Bitmap? img;

        public PNG_EXTRACTOR(string pathToPNG, string[] pathToResultPNGs)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            #region | STEP 1 - Color Difference with LAB |

            img = new Bitmap(pathToPNG);
            height = img.Height;
            width = img.Width;

            BitmapData imageData = img.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            byte[] imageBytes = new byte[Math.Abs(imageData.Stride) * height];
            int[] pixelInts = new int[imageBytes.Length / 3];
            IntPtr scan0 = imageData.Scan0;
            Marshal.Copy(scan0, imageBytes, 0, imageBytes.Length);

            pixelCount = pixelInts.Length;
            int a = 0;
            activatedPixels = new bool[pixelCount];
            lookedThroughPixels = new bool[pixelCount];
            bool[] activatedPixelsSaveState = new bool[pixelCount];
            bool[] activatedPixelsSaveState2 = new bool[pixelCount];
            pixelRowVals = new int[pixelCount];
            pixelColumnVals = new int[pixelCount];
            pixelToLeftVals = new int[pixelCount];
            pixelToRightVals = new int[pixelCount];

            for (int i = 0; i < pixelCount; i++)
            {
                pixelInts[i] = imageBytes[a] | (imageBytes[a + 1] << 8) | (imageBytes[a + 2] << 16);
                a += 3;
            }

            int byteA = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                int tPix = pixelInts[i];
                if ((i + 1) % width != 0 && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference(tPix, pixelInts[i + 1])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else if (i % width != 0 && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference(tPix, pixelInts[i - 1])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else if (i + width < pixelCount && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference(tPix, pixelInts[i + width])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else if (width <= i && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference(tPix, pixelInts[i - width])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;

                activatedPixels[i] = imageBytes[byteA] == edgeColorByte;

                byteA += 3;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[0], ImageFormat.Png);
            img.UnlockBits(imageData);
            activatedPixelsSaveState = (bool[])activatedPixels.Clone();

            #endregion

            #region | STEP 2 - Main Connected Area |

            Console.WriteLine("STEP 1 - Color Difference with LAB   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Restart();
            int recordSize = 0, recordSizeSecondary = 0;
            List<int> recordField = new List<int>();
            List<List<int>> allFields = new List<List<int>>();
            List<int> squareSizes = new List<int>();

            for (int i = 0; i < pixelCount; i++)
            {
                int tRow = (i - i % width) / width, tColumn = i % width;
                pixelRowVals[i] = tRow;
                pixelColumnVals[i] = tColumn;
                pixelToLeftVals[i] = -1;
                pixelToRightVals[i] = -1;
            }

            for (int i = 0; i < pixelCount; i++)
            {
                if (lookedThroughPixels[i])
                {
                    continue;
                }

                int tRow = pixelRowVals[i], tColumn = pixelColumnVals[i];

                int tLowX = tRow, tHighX = tRow, tLowY = tColumn, tHighY = tColumn;

                if (activatedPixels[i])
                {
                    List<int> tField = new List<int>();
                    RecursiveCleanUp(ref tField, ref tLowX, ref tHighX, ref tLowY, ref tHighY, i);

                    allFields.Add(tField);

                    int tSize = (tHighX - tLowX) * (tHighY - tLowY);
                    squareSizes.Add(tSize);
                    if (CAM_SETTINGS.MAIN_AREA_SELECTION_WITH_PIXELCOUNT) tSize = tField.Count;

                    //Console.WriteLine(tField.Count);
                    if (tSize > recordSize)
                    {
                        recordSizeSecondary = (tHighX - tLowX) * (tHighY - tLowY);
                        recordField = tField;
                        recordSize = tSize;
                    }
                }
            }

            int rfc = recordField.Count;
            bool[] tactPixels = new bool[pixelCount];
            for (int i = 0; i < rfc; i++)
            {
                tactPixels[recordField[i]] = true;
            }

            List<(int, int)> checkPoints = new List<(int, int)>();
            for (int i = 0; i < width; i += 3)
            {
                int tA = 0;
                while (tA < pixelCount && !tactPixels[tA]) tA += width;
                if (tA < pixelCount) checkPoints.Add((pixelColumnVals[tA], pixelRowVals[tA]));
                tA = pixelCount - i - 1;
                while (tA > -1 && !tactPixels[tA]) tA -= width;
                if (tA > -1) checkPoints.Add((pixelColumnVals[tA], pixelRowVals[tA]));
            }

            int testtest = 0;
            int afc = allFields.Count, cpc = checkPoints.Count;
            for (int i = 0; i < afc; i++)
            {
                int tC = allFields[i].Count;
                if (tC < CAM_SETTINGS.MAIN_AREA_ADDITION_MIN_SIZE || squareSizes[i] < recordSizeSecondary) continue;

                for (int j = 0; j < tC; j++)
                {
                    int tidx = allFields[i][j];
                    int tX = pixelColumnVals[tidx], tY = pixelRowVals[tidx];

                    for (int c = 0; c < cpc; c++)
                    {

                        testtest++;

                        if (EfficientAlternativeToVec2IntTupleDistance((tX, tY), checkPoints[c]) < CAM_SETTINGS.MAIN_AREA_ADDITION_DIST_THRESHOLD)
                        {
                            for (int k = 0; k < tC; k++)
                            {
                                tactPixels[allFields[i][k]] = true;
                            }

                            goto ContinueWithOtherAreas;
                        }

                    }
                }

            ContinueWithOtherAreas:;
            }

            //Console.WriteLine(cpc);
            //Console.WriteLine(testtest);

            byteA = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                if (tactPixels[i])
                {
                    activatedPixels[i] = true;
                    imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                }
                else
                {
                    activatedPixels[i] = false;
                    imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;
                }

                lookedThroughPixels[i] = false;
                byteA += 3;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[1], ImageFormat.Png);
            activatedPixelsSaveState2 = (bool[])activatedPixels.Clone();

            #endregion

            #region | STEP 3 - Recognizable Square Selection |

            Console.WriteLine("STEP 2 - Main Connected Area   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Restart();
            for (int i = 0; i < width; i++)
            {
                if (lookedThroughPixels[i] || activatedPixels[i])
                {
                    continue;
                }
                List<int> fillL = new List<int>();
                FillReplacementDetermination(ref fillL, i);
                int tC = fillL.Count;
                for (int j = 0; j < tC; j++)
                {
                    activatedPixels[fillL[j]] = true;
                    int tA = fillL[j] * 3;
                    imageBytes[tA] = imageBytes[tA + 1] = imageBytes[tA + 2] = edgeColorByte;
                }
            }

            for (int i = 0; i < pixelCount; i++) lookedThroughPixels[i] = false;

            int tminDistToMid = 100000;
            int ssqC = 0;
            int ssqHorizontalX = 0, ssqHorizontalY = 0, ssqAvrgHorY = 0;
            int ssqVerticalX = 0;
            List<int> vertlineidxs = new List<int>();
            List<(double, int)> securevertlines = new List<(double, int)>();

            for (int i = 0; i < pixelCount; i++)
            {
                if (lookedThroughPixels[i] || activatedPixels[i])
                {
                    continue;
                }
                List<int> fillL = new List<int>();
                if (FillReplacementDetermination(ref fillL, i))
                {
                    int tC = fillL.Count;
                    for (int j = 0; j < tC; j++)
                    {
                        activatedPixels[fillL[j]] = true;
                        int tA = fillL[j] * 3;
                        imageBytes[tA] = imageBytes[tA + 1] = imageBytes[tA + 2] = 0b11110000;
                    }
                }
                else
                {
                    int tC = fillL.Count;
                    int tavrgRow = 0, tavrgCol = 0;

                    for (int j = 0; j < tC; j++)
                    {
                        int tRow = pixelRowVals[fillL[j]], tColumn = pixelColumnVals[fillL[j]];

                        tavrgRow += tRow;
                        tavrgCol += tColumn;
                    }

                    tavrgCol /= tC;
                    tavrgRow /= tC;

                    (int, int)[] edgePoints = new (int, int)[4];
                    int[] edgePointDists = new int[4] { -1, -1, -1, -1 };

                    for (int j = 0; j < tC; j++)
                    {
                        int tRow = pixelRowVals[fillL[j]], tColumn = pixelColumnVals[fillL[j]];

                        int tidx;
                        if (tRow > tavrgRow) tidx = tColumn > tavrgCol ? 0 : 3;
                        else tidx = tColumn > tavrgCol ? 1 : 2;

                        int tsqPosYDist = tRow - tavrgRow, tsqPosXDist = tColumn - tavrgCol;
                        int t_ti_D;

                        if ((t_ti_D = (int)Math.Sqrt(tsqPosYDist * tsqPosYDist + tsqPosXDist * tsqPosXDist)) > edgePointDists[tidx])
                        {
                            edgePointDists[tidx] = t_ti_D;
                            edgePoints[tidx] = (tColumn, tRow);
                        }
                    }

                    int sqAvrgXPosDist = tavrgCol - CAM_SETTINGS.SQUARE_POSX_PREFERATION;
                    int sqAvrgYPosDist = tavrgRow - CAM_SETTINGS.SQUARE_POSY_PREFERATION;
                    int tD;

                    ssqHorizontalX += edgePoints[1].Item1 - edgePoints[2].Item1 + edgePoints[0].Item1 - edgePoints[3].Item1;
                    ssqHorizontalY += edgePoints[1].Item2 - edgePoints[2].Item2 + edgePoints[0].Item2 - edgePoints[3].Item2;

                    ssqC++;

                    double tLine1Ye1X, tLine2Ye1X;
                    int txvertPos1 =
                    GetStartXPosOfVerticalLine(
                        tLine1Ye1X = Scale2DoubleTupleYTo(
                            (edgePoints[0].Item1 - edgePoints[1].Item1, edgePoints[0].Item2 - edgePoints[1].Item2),
                            1d
                        ).Item1,
                        edgePoints[1]
                    );

                    int txvertPos2 =
                    GetStartXPosOfVerticalLine(
                        tLine2Ye1X = Scale2DoubleTupleYTo(
                            (edgePoints[3].Item1 - edgePoints[2].Item1, edgePoints[3].Item2 - edgePoints[2].Item2),
                            1d
                        ).Item1,
                        edgePoints[2]
                    );

                    ssqVerticalX += Math.Abs(txvertPos1 - txvertPos2);

                    vertlineidxs.AddRange(GetVerticalLine(tLine1Ye1X, txvertPos1));
                    vertlineidxs.AddRange(GetVerticalLine(tLine2Ye1X, txvertPos2));

                    securevertlines.Add((tLine1Ye1X, txvertPos1));
                    securevertlines.Add((tLine2Ye1X, txvertPos2));

                    versqstartpos[txvertPos1] = true;
                    versqstartpos[txvertPos2] = true;
                    versqstartposd[txvertPos1] = tLine1Ye1X;
                    versqstartposd[txvertPos2] = tLine2Ye1X;

                    int txhorPos1 =
                    GetStartYPosOfHorizontalLine(
                        tLine1Ye1X = Scale2DoubleTupleXTo(
                            (edgePoints[1].Item1 - edgePoints[2].Item1, edgePoints[1].Item2 - edgePoints[2].Item2),
                            1d
                        ).Item2,
                        edgePoints[2]
                    );

                    int txhorPos2 =
                    GetStartYPosOfHorizontalLine(
                        tLine2Ye1X = Scale2DoubleTupleXTo(
                            (edgePoints[0].Item1 - edgePoints[3].Item1, edgePoints[0].Item2 - edgePoints[3].Item2),
                            1d
                        ).Item2,
                        edgePoints[0]
                    );

                    //Console.WriteLine(txhorPos1 + " | " + txhorPos2);
                    //Console.WriteLine(edgePoints[0] + " | " + edgePoints[3] + " => " + (edgePoints[0].Item1 - edgePoints[3].Item1, edgePoints[0].Item2 - edgePoints[3].Item2) + " => " + txhorPos2);
                    //Console.WriteLine(tLine1Ye1X);
                    //Console.WriteLine((edgePoints[1].Item1 - edgePoints[2].Item1, edgePoints[1].Item2 - edgePoints[2].Item2));
                    //Console.WriteLine(tLine2Ye1X);
                    //Console.WriteLine((edgePoints[0].Item1 - edgePoints[3].Item1, edgePoints[0].Item2 - edgePoints[3].Item2));

                    horsqstartpos[txhorPos1] = true;
                    horsqstartpos[txhorPos2] = true;
                    horsqstartposd[txhorPos1] = tLine1Ye1X;
                    horsqstartposd[txhorPos2] = tLine2Ye1X;

                    ssqAvrgHorY += Math.Abs(txhorPos1 - txhorPos2);
                    if ((tD = (int)Math.Sqrt(sqAvrgXPosDist * sqAvrgXPosDist + sqAvrgYPosDist * sqAvrgYPosDist)) < tminDistToMid)
                    {
                        tminDistToMid = tD;
                    }
                }
            }

            ssqHorizontalX /= ssqC * 2;
            ssqHorizontalY /= ssqC * 2;
            ssqVerticalX /= ssqC;
            ssqAvrgHorY /= ssqC;

            double xe1HorLineVecY = Scale2DoubleTupleXTo(Normalize2IntTuple((ssqHorizontalX, ssqHorizontalY)), 1).Item2;

            double[] vertlineLinRegIdxArr = new double[securevertlines.Count];
            double[] vertlineLinRegValArr = new double[securevertlines.Count];

            for (int i = 0; i < securevertlines.Count; i++)
            {
                vertlineLinRegIdxArr[i] = securevertlines[i].Item2;
                vertlineLinRegValArr[i] = securevertlines[i].Item1;
            }

            (double, double) angleCorrelationVertLines = LinearRegression(vertlineLinRegIdxArr, vertlineLinRegValArr);

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[2], ImageFormat.Png);

            byteA = 0;
            for (int i = 0; i < pixelCount; i++, byteA += 3) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;
            for (int i = 0; i < vertlineidxs.Count; i++)
            {
                int tidx = vertlineidxs[i];
                byteA = tidx * 3;
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[8], ImageFormat.Png);

            #endregion

            #region | STEP 4 - Vertical Lines |

            Console.WriteLine("STEP 3 - Recognizable Square Selection   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Restart();
            for (int i = 0; i < pixelCount; i++)
            {
                if ((pixelToLeftVals[i] = (i % width == 0 || !activatedPixelsSaveState2[i]) ? 0 : (pixelToLeftVals[i - 1] + 1)) > CAM_SETTINGS.MAX_LINE_WIDTH || !activatedPixelsSaveState2[i]) activatedPixels[i] = false;
                int idx2 = pixelCount - i - 1;
                if ((pixelToRightVals[idx2] = ((idx2 + 1) % width == 0 || !activatedPixelsSaveState2[idx2]) ? 0 : (pixelToRightVals[idx2 + 1] + 1)) > CAM_SETTINGS.MAX_LINE_WIDTH) activatedPixels[idx2] = false;
                lookedThroughPixels[i] = false;
            }

            for (int i = 0; i < pixelCount; i++)
            {
                pixelToLeftVals[i] = pixelToRightVals[i] = -1;
                if (lookedThroughPixels[i])
                {
                    continue;
                }

                int tRow = pixelRowVals[i], tColumn = pixelColumnVals[i];

                int tLowX = tRow, tHighX = tRow, tLowY = tColumn, tHighY = tColumn;

                if (activatedPixels[i])
                {
                    List<int> tField = new List<int>();
                    RecursiveCleanUp(ref tField, ref tLowX, ref tHighX, ref tLowY, ref tHighY, i);
                    int tSize = tField.Count;
                    if (tSize <= CAM_SETTINGS.MAX_DISTORTION_SIZE)
                    {
                        for (int j = 0; j < tSize; j++)
                        {
                            activatedPixels[tField[j]] = false;
                        }
                    }
                }
            }

            double[] vertlinePrecentageVals = new double[width];
            int tvertlidx = 0;
            double thighestvertl = 0d;
            for (int i = CAM_SETTINGS.VERTICALLINE_MIN_PIC_END_DIST; i < width - CAM_SETTINGS.VERTICALLINE_MIN_PIC_END_DIST; i++)
            {
                if ((vertlinePrecentageVals[i] = GetFilledPrecentageOfVerticalLine(angleCorrelationVertLines.Item1 * i + angleCorrelationVertLines.Item2, i)) > thighestvertl)
                {
                    tvertlidx = i;
                    thighestvertl = vertlinePrecentageVals[i];
                }
            }

            int[] vertlidxs = new int[9] { tvertlidx, 0, 0, 0, 0, 0, 0, 0, 0 };
            int vCurAddLB = ssqVerticalX, vCurAddHB = ssqVerticalX;
            int tvLBpos = tvertlidx, tvHBpos = tvertlidx;

            for (int i = 0; i < 8; i++)
            {
                int tLBPOS = tvLBpos - vCurAddLB;
                int tHBPOS = tvHBpos + vCurAddHB;
                int tpos = -1;
                double thighest = -1;
                bool b = false;

                for (int j = -CAM_SETTINGS.VERTICALLINE_MAX_DEVIATION_FROM_AVERAGE; j < CAM_SETTINGS.VERTICALLINE_MAX_DEVIATION_FROM_AVERAGE + 1; j++)
                {
                    double LBprecentage = tLBPOS + j > -1 ? vertlinePrecentageVals[tLBPOS + j] : -1;
                    double HBprecentage = tHBPOS + j < width - CAM_SETTINGS.VERTICALLINE_MIN_PIC_END_DIST ? vertlinePrecentageVals[tHBPOS + j] : -1;
                    if (LBprecentage > thighest)
                    {
                        thighest = LBprecentage;
                        tpos = tLBPOS + j;
                        b = true;
                    }
                    if (HBprecentage > thighest)
                    {
                        thighest = HBprecentage;
                        tpos = tHBPOS + j;
                        b = false;
                    }
                }

                if (b)
                {
                    vCurAddLB = Math.Abs(tpos - tvLBpos);
                    tvLBpos = tpos;
                }
                else
                {
                    vCurAddHB = Math.Abs(tpos - tvHBpos);
                    tvHBpos = tpos;
                }

                vertlidxs[i + 1] = tpos;
            }

            List<int> pixIdxs2 = new List<int>();
            byteA = 0;
            for (int i = 0; i < pixelCount; i++, byteA += 3) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;
            for (int i = 0; i < 9; i++)
            {
                List<int> tLine = GetVerticalLine(angleCorrelationVertLines.Item1 * vertlidxs[i] + angleCorrelationVertLines.Item2, vertlidxs[i]);
                pixIdxs2.AddRange(tLine);
                finalVertLinePoses[i] = tLine;
            }
            for (int i = 0; i < pixIdxs2.Count; i++)
            {
                int tidx = pixIdxs2[i];
                byteA = tidx * 3;
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[10], ImageFormat.Png);

            byteA = 0;
            for (int i = 0; i < pixelCount; i++, byteA += 3)
            {
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = activatedPixels[i] ? edgeColorByte : bgColorByte;
                activatedPixels[i] = true;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[3], ImageFormat.Png);

            byteA = 0; for (int i = 0; i < pixelCount; i++, byteA += 3) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;
            for (int i = 0; i < verlineids.Count; i++)
            {
                int tidx = verlineids[i];
                byteA = tidx * 3;
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = verlineidstrengths[i];
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[9], ImageFormat.Png);

            #endregion

            #region | STEP 5 - Horizontal Lines |

            Console.WriteLine("STEP 4 - Vertical Lines   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Restart();
            for (int i = 0; i < pixelCount; i++)
            {
                int idx1 = i;
                int idx2 = pixelCount - idx1 - 1;
                if ((pixelToRightVals[idx2] = (idx2 + width >= pixelCount || !activatedPixelsSaveState2[idx2]) ? 0 : (pixelToRightVals[idx2 + width] + 1)) > CAM_SETTINGS.MAX_LINE_WIDTH) activatedPixels[idx2] = false;
                if ((pixelToLeftVals[idx1] = (idx1 - width < 0 || !activatedPixelsSaveState2[idx1]) ? 0 : (pixelToLeftVals[idx1 - width] + 1)) > CAM_SETTINGS.MAX_LINE_WIDTH || !activatedPixelsSaveState2[idx1]) activatedPixels[idx1] = false;
                lookedThroughPixels[i] = false;
            }

            for (int i = 0; i < pixelCount; i++)
            {
                pixelToLeftVals[i] = pixelToRightVals[i] = -1;
                if (lookedThroughPixels[i])
                {
                    continue;
                }

                int tRow = pixelRowVals[i], tColumn = pixelColumnVals[i];

                int tLowX = tRow, tHighX = tRow, tLowY = tColumn, tHighY = tColumn;

                if (activatedPixels[i])
                {
                    List<int> tField = new List<int>();
                    RecursiveCleanUp(ref tField, ref tLowX, ref tHighX, ref tLowY, ref tHighY, i);
                    int tSize = tField.Count;
                    if (tSize <= CAM_SETTINGS.MAX_DISTORTION_SIZE)
                    {
                        for (int j = 0; j < tSize; j++)
                        {
                            activatedPixels[tField[j]] = false;
                        }
                    }
                }
            }

            int l = -1;
            List<int> distsBetweenLines = new List<int>();
            List<int> linePositions = new List<int>();
            List<(int, int)> positionsBetweenLines = new List<(int, int)>();
            int until = height; // - (int)(xe1HorLineVecY * 400)
            double toverallhighest = 0d;
            int toverallhighestidx = 0;
            double[] horlinePrecentageVals = new double[until];

            for (int i = 0; i < until; i++)
            {
                double tval;
                if ((tval = horlinePrecentageVals[i] = GetFilledPrecentageOfHorizontalLine(xe1HorLineVecY, i)) > CAM_SETTINGS.HORIZONTALLINE_FULL_SECURE_DETERMINANT)
                {
                    double thighest = tval;
                    int tidx = i;

                    for (int j = 0; j < CAM_SETTINGS.HORIZONTALLINE_MIN_DIST; j++)
                    {
                        if (++i == until) break;
                        double ttval = horlinePrecentageVals[i] = GetFilledPrecentageOfHorizontalLine(xe1HorLineVecY, i);
                        if (ttval > thighest)
                        {
                            thighest = ttval;
                            tidx = i;
                            if (thighest > toverallhighest)
                            {
                                toverallhighestidx = tidx;
                                toverallhighest = thighest;
                            }
                        }
                    }

                    horlinePrecentageVals[tidx] = thighest;

                    if (l != -1)
                    {
                        int tv = tidx - l;
                        distsBetweenLines.Add(tv);
                        positionsBetweenLines.Add((l, tidx));
                    }

                    linePositions.Add(l = tidx);
                }
                //else horlinePrecentageVals[i] = tval;
            }

            int[] tsortidxarr = new int[until];

            string str = "DatenFunktion({";
            for (int i = 0; i < until; i++)
            {
                tsortidxarr[i] = i;
                if (horlinePrecentageVals[i] < 0.02) horlinePrecentageVals[i] = 0;

                str += i + (i == until - 1 ? "},{" : ",");
            }

            double[] tintegralVals = new double[until];

            for (int i = 0; i < until; i++)
            {
                double tintegral = 0;
                for (int j = -(CAM_SETTINGS.HORIZONTALLINE_INTEGRAL_SIZE - 1); j <= CAM_SETTINGS.HORIZONTALLINE_INTEGRAL_SIZE; j++)
                {
                    int t = i + j;
                    if (t > 0 && t < until)
                    {
                        double v1 = horlinePrecentageVals[t - 1];
                        double v2 = horlinePrecentageVals[t];

                        if (v1 < v2) {
                            double d = v2;
                            v2 = v1;
                            v1 = d;
                        }

                        tintegral += (v1 - v2) / 2d + v2;
                    }
                }

                tintegralVals[i] = tintegral;

                str += horlinePrecentageVals[i].ToString().Replace(",", ".") + (i == until - 1 ? "})" : ",");
            }

            Array.Sort(tintegralVals, tsortidxarr);
            Array.Reverse(tintegralVals);
            Array.Reverse(tsortidxarr);


            Console.WriteLine(str);

            bool[] activatedPixelsSaveState3 = new bool[pixelCount];
            activatedPixelsSaveState3 = (bool[])activatedPixels.Clone();

            byteA = 0;
            for (int i = 0; i < pixelCount; i++, byteA += 3)
            {
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = activatedPixels[i] ? edgeColorByte : bgColorByte;
                activatedPixels[i] = false;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[4], ImageFormat.Png);


            byteA = 0; for (int i = 0; i < pixelCount; i++, byteA += 3) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;
            for (int i = 0; i < horlineids.Count; i++)
            {
                int tidx = horlineids[i];
                byteA = tidx * 3;
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = horlineidstrengths[i];
                activatedPixels[tidx] = true;
            }


            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[6], ImageFormat.Png);

            int tHPos = toverallhighestidx, tLPos = toverallhighestidx;
            int tHPosAdd = ssqAvrgHorY, tLPosAdd = ssqAvrgHorY;

            //Console.WriteLine(tLPos);

            int[] furtherLines = new int[9] { toverallhighestidx, 0, 0, 0, 0, 0, 0, 0, 0 };

            bool[] tusedUpVals = new bool[until];

            for (int i = 0, k = 0; k < 9; i++)
            {
                int tidx = tsortidxarr[i];
                for (int j = -CAM_SETTINGS.HORIZONTALLINE_INTEGRAL_MIN_DIST; j <= CAM_SETTINGS.HORIZONTALLINE_INTEGRAL_MIN_DIST; j++)
                    if (tidx + j > -1 && tidx + j < until && tusedUpVals[tidx + j]) goto SkipThisIntegral;
                Console.WriteLine(tidx + " -> " + tintegralVals[i]);
                furtherLines[k++] = tidx; 
                for (int j = -CAM_SETTINGS.HORIZONTALLINE_INTEGRAL_MIN_DIST; j <= CAM_SETTINGS.HORIZONTALLINE_INTEGRAL_MIN_DIST; j++)
                {
                    if (tidx + j > -1 && tidx + j < until) tusedUpVals[tidx + j] = true;
                }
            SkipThisIntegral:;
            }

            List<int> pixIdxs = new List<int>();

            /*for (int i = 0; i < 8; i++)
            {
                int tLBPOS = tLPos - tLPosAdd;
                int tHBPOS = tHPos + tHPosAdd;
                int tpos = -1;
                double thighest = -1;
                bool b = false;

                Console.WriteLine(tLPos + " -> " + tLBPOS + " | " + tHPos + "-> " + tHBPOS);
                //
                //List<(int, int)> tLinRegPointsL = tLBPOS > -1 ? GetHorizontalLineLinearRegressionOptimizorPoints(xe1HorLineVecY, tLBPOS, activatedPixels) : new List<(int, int)>();
                //List<(int, int)> tLinRegPointsH = tHBPOS < height ? GetHorizontalLineLinearRegressionOptimizorPoints(xe1HorLineVecY, tHBPOS, activatedPixels) : new List<(int, int)>();
                //
                //b = tLinRegPointsH.Count < tLinRegPointsL.Count;
                //
                //tpos = b ? tLBPOS : tHBPOS;

                //int t_ti_C = tLinRegPoints2.Count;
                //if (tLinRegPoints3.Count > t_ti_C) {
                //    t_ti_C = tLinRegPoints3.Count;
                //    tLinRegPoints = tLinRegPoints3;
                //}
                //else tLinRegPoints = tLinRegPoints2;
                //
                //int[] tlinregidxs = new int[t_ti_C];
                //int[] tlinregvals = new int[t_ti_C];
                //for (int j = 0; j < t_ti_C; j++)
                //{
                //    tlinregidxs[j] = tLinRegPoints[j].Item1;
                //    tlinregvals[j] = tLinRegPoints[j].Item2;
                //}
                //(double, double) tlinregres = LinearRegression(tlinregidxs, tlinregvals);
                //
                //
                //for (int x = 0; x < width; x++)
                //{
                //    int tID = (int)(tlinregres.Item1 * x + tlinregres.Item2) * width + x;
                //    if (x == 0) finalHorLinePoses[i] = new List<int>();
                //    pixIdxs.Add(tID);
                //    finalHorLinePoses[i].Add(tID);
                //}


                int searchWindowSizeIncr = 0;

                while (thighest < 0.1 && searchWindowSizeIncr < 100) {

                    ++searchWindowSizeIncr;

                    for (int j = -CAM_SETTINGS.HORIZONTALLINE_MAX_DEVIATION_FROM_AVERAGE_DOWN; j < CAM_SETTINGS.HORIZONTALLINE_MAX_DEVIATION_FROM_AVERAGE_UP * searchWindowSizeIncr + 1; j++)
                    {
                        //Console.WriteLine(tLBPOS + j + " | " +);
                        double LBprecentage = tLBPOS + j > -1 && tLBPOS + j < tLPos - 15 ? horlinePrecentageVals[tLBPOS + j] : -1;
                        double HBprecentage = tHBPOS + j < until && tHBPOS + j > tHPos + 15 ? horlinePrecentageVals[tHBPOS + j] : -1;
                        if (LBprecentage > thighest)
                        {
                            thighest = LBprecentage;
                            tpos = tLBPOS + j;
                            b = true;
                        }
                        if (HBprecentage > thighest)
                        {
                            thighest = HBprecentage;
                            tpos = tHBPOS + j;
                            b = false;
                        }
                    }

                    //Console.WriteLine(":)");

                }

                if (b)
                {
                    tLPosAdd = Math.Abs(tpos - tLPos);
                    tLPos = tpos;
                }
                else
                {
                    tHPosAdd = Math.Abs(tpos - tHPos);
                    tHPos = tpos;
                }

                //Console.WriteLine(thighest + ": " + tpos);

                furtherLines[i + 1] = tpos;
            }*/

            bool[] bbbbbb = new bool[pixelCount];

            byteA = 0;
            for (int i = 0; i < pixelCount; i++, byteA += 3) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;
            
            for (int i = 0; i < furtherLines.Length; i++)
            {
                List<(int, int)> tLinRegPoints = GetHorizontalLineLinearRegressionOptimizorPoints(xe1HorLineVecY, furtherLines[i], activatedPixelsSaveState3);
                int t_ti_C = tLinRegPoints.Count;
            
                if (t_ti_C < CAM_SETTINGS.HORIZONTALLINE_MIN_LINREG_OPTIMIZATION_POINTS)
                {
                    //Console.WriteLine(i);
                    //tLinRegPoints = GetHorizontalLineLinearRegressionOptimizorPoints(xe1HorLineVecY, furtherLines[i], activatedPixelsSaveState);
                    //t_ti_C = tLinRegPoints.Count;
                    //if (t_ti_C < CAM_SETTINGS.HORIZONTALLINE_MIN_LINREG_OPTIMIZATION_POINTS)
                    //{
                    List<int> tLine = GetHorizontalLine(xe1HorLineVecY, furtherLines[i]); // Theoretisch xe1HorLineVecY manchmal austauschen; aber dieser Teil sollte eig sowieso nie vorkommen
                    finalHorLinePoses[i] = tLine;
                    pixIdxs.AddRange(tLine);
                    continue;
                    //}
                }
            
                int[] tlinregidxs = new int[t_ti_C];
                int[] tlinregvals = new int[t_ti_C];
                bool differentValueUpUntilHere = false;
                int lVal = 0;
                for (int j = 0; j < t_ti_C; j++)
                {
                    tlinregidxs[j] = tLinRegPoints[j].Item1;
                    tlinregvals[j] = tLinRegPoints[j].Item2;

                    if (j != 0 && lVal != tlinregvals[j])
                    {
                        differentValueUpUntilHere = true;
                    }

                    lVal = tlinregvals[j];

                    bbbbbb[tlinregidxs[j] + tlinregvals[j] * width] = true;
                }

                if (!differentValueUpUntilHere) tlinregvals[t_ti_C - 1]++;

                (double, double) tlinregres = LinearRegression(tlinregidxs, tlinregvals);

                for (int x = 0; x < width; x++)
                {
                    int tID = (int)(tlinregres.Item1 * x + tlinregres.Item2) * width + x;
                    if (x == 0)
                    {
                        finalHorLinePoses[i] = new List<int>();
                    }
                    pixIdxs.Add(tID);
                    finalHorLinePoses[i].Add(tID);
                }
            }


            for (int i = 0; i < pixIdxs.Count; i++)
            {
                int tidx = pixIdxs[i];
                byteA = tidx * 3;
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[7], ImageFormat.Png);


            byteA = 0;
            for (int i = 0; i < pixelCount; i++, byteA += 3)
            {
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bbbbbb[i] ? edgeColorByte : bgColorByte;
                activatedPixels[i] = false;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[5], ImageFormat.Png);

            #endregion

            #region | STEP 6 - Final Squares |

            Console.WriteLine("STEP 5 - Horizontal Lines   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Restart();
            int tL = pixIdxs.Count;
            int overlapPoints = 0, sumOfXOP = 0, sumOfYOP = 0;
            (int, int)[] OP_POINTS = new (int, int)[81];
            byteA = 0;
            for (int i = 0; i < pixelCount; i++, byteA += 3)
            {
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;
                activatedPixels[i] = false;
            }
            for (int i = 0; i < tL; i++)
            {
                bool bbb;
                if ((bbb = IntListContains(pixIdxs2, pixIdxs[i])) || (pixIdxs[i] + width < pixelCount && IntListContains(pixIdxs2, pixIdxs[i] + width)))
                {
                    int tidx = pixIdxs[i];
                    if (!bbb) tidx += width;
                    activatedPixels[tidx] = true;

                    for (int o = 0; o < 81; o++)
                    {
                        if (Vec2IntTupleDistance((pixelColumnVals[tidx], pixelRowVals[tidx]), OP_POINTS[o]) < 10)
                        {
                            //Console.WriteLine((pixelColumnVals[tidx], pixelRowVals[tidx]));
                            goto SkipThisOverlap;
                        }
                    }

                    OP_POINTS[overlapPoints++] = (pixelColumnVals[tidx], pixelRowVals[tidx]);

                    sumOfXOP += pixelColumnVals[tidx];
                    sumOfYOP += pixelRowVals[tidx];
                    byteA = tidx * 3;
                    imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                SkipThisOverlap:;
                }
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[11], ImageFormat.Png);

            sumOfXOP /= overlapPoints;
            sumOfYOP /= overlapPoints;

            for (int i = 0; i < pixelCount; i++)
            {
                activatedPixels[i] = false;
                lookedThroughPixels[i] = false;
            }

            for (int i = 0; i < tL; i++)
            {
                int tidx = pixIdxs[i];
                activatedPixels[tidx] = true;
                byteA = tidx * 3;
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
            }
            for (int i = 0; i < pixIdxs2.Count; i++)
            {
                int tidx = pixIdxs2[i];
                activatedPixels[tidx] = true;
                byteA = tidx * 3;
                imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[12], ImageFormat.Png);

            List<List<int>> finalSquares = new List<List<int>>();
            for (int i = 0; i < pixelCount; i++)
            {
                if (lookedThroughPixels[i] || activatedPixels[i])
                {
                    continue;
                }
                List<int> fillL = new List<int>();
                if (FillEdgeReplacementDetermination(ref fillL, i)) continue;

                finalSquares.Add(fillL);

                int tC = fillL.Count;
                for (int j = 0; j < tC; j++)
                {
                    activatedPixels[fillL[j]] = true;
                    int tA = fillL[j] * 3;
                    imageBytes[tA] = imageBytes[tA + 1] = imageBytes[tA + 2] = 0b1110000;
                }
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[13], ImageFormat.Png);

            #endregion

            #region | STEP 7 - Piece Recognition |

            Console.WriteLine("STEP 6 - Final Squares   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Restart();

            for (int i = 0; i < pixelCount; i++) activatedPixels[i] = false;


            byteA = 0;
            for (int i = 0; i < pixelCount; i++)
            {
                int tPix = pixelInts[i];
                if ((i + 1) % width != 0 && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference2(tPix, pixelInts[i + 1])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else if (i % width != 0 && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference2(tPix, pixelInts[i - 1])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else if (i + width < pixelCount && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference2(tPix, pixelInts[i + width])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else if (width <= i && STATIC_MAIN_CAMERA_ANALYSER.ColorDifference2(tPix, pixelInts[i - width])) imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = edgeColorByte;
                else imageBytes[byteA] = imageBytes[byteA + 1] = imageBytes[byteA + 2] = bgColorByte;

                activatedPixels[i] = imageBytes[byteA] == edgeColorByte;

                byteA += 3;
            }

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[15], ImageFormat.Png);
            activatedPixelsSaveState = (bool[])activatedPixels.Clone();



            int fsqC = finalSquares.Count;


            for (int i = 0; i < pixelCount; i++) activatedPixelsSaveState2[i] = false;

            double[] dets = new double[fsqC];
            double[] pixelDetVal = new double[pixelCount];

            for (int s = 0; s < fsqC; s++)
            {
                List<int> tsquare = finalSquares[s];
                int tC = tsquare.Count;

                int txsum = 0, tysum = 0;

                //int tID = tsquare[tC / 2];
                //for (int i = 0; i < OPPC; i++) {
                //
                //    if (Vec2IntTupleDistance((pixelColumnVals[tID], pixelRowVals[tID]), OP_POINTS[i]) < 5)
                //    {
                //
                //    }
                //}


                for (int i = 0; i < tC; i++)
                {
                    int tID = tsquare[i];
                    txsum += pixelColumnVals[tID];
                    tysum += pixelRowVals[tID];
                }
                txsum /= tC;
                tysum /= tC;

                //int tA = (tysum * width + txsum) * 3;
                //imageBytes[tA] = imageBytes[tA + 1] = imageBytes[tA + 2] = 0b1111110;

                double[] thDists = new double[4];
                int[] tIDs = new int[4];
                for (int i = 0; i < tC; i++)
                {
                    int tID = tsquare[i], tV;
                    int tX = pixelColumnVals[tID];
                    int tY = pixelRowVals[tID];
                    if (tX > txsum)
                    {
                        if (tY > tysum) tV = 0;
                        else tV = 1;
                    }
                    else
                    {
                        if (tY > tysum) tV = 3;
                        else tV = 2;
                    }

                    double d = Vec2IntTupleDistance((tX, tY), (txsum, tysum));

                    if (d > thDists[tV])
                    {
                        thDists[tV] = d;
                        tIDs[tV] = tID;
                    }
                }

                int tCenterID = tysum * width + txsum;
                int tCenterColor = pixelInts[tCenterID];
                int[] tFurtherColors = new int[9] { 0, 0, 0, 0, tCenterColor, 0, 0, 0, 0 };
                int[] tFurtherIDs = new int[4] { 0, 0, 0, 0 };

                for (int i = 0; i < 4; i++)
                {
                    int tID = tIDs[i];
                    int tX = pixelColumnVals[tID], tY = pixelRowVals[tID];

                    int pointIDBetweenCenterAndEdge = (int)((txsum - tX) / 3.14) + tX + (int)(((tysum - tY) / 3.14) + tY) * width;

                    tFurtherIDs[i] = pointIDBetweenCenterAndEdge;

                    //imageBytes[pointIDBetweenCenterAndEdge * 3] = imageBytes[pointIDBetweenCenterAndEdge * 3 + 1] = imageBytes[pointIDBetweenCenterAndEdge * 3 + 2] = 0b1111110;

                    tFurtherColors[i] = pixelInts[pointIDBetweenCenterAndEdge];
                }

                for (int i = 0; i < 4; i++)
                {
                    int nxt = (i + 1) % 4;
                    int tID = tFurtherIDs[i], tID2 = tFurtherIDs[nxt];
                    int tX = pixelColumnVals[tID], tY = pixelRowVals[tID];
                    int tX2 = pixelColumnVals[tID2], tY2 = pixelRowVals[tID2];

                    int pointIDBetweenCenterAndEdge = ((tX - tX2) / 2) + tX2 + (((tY - tY2) / 2) + tY2) * width;

                    //imageBytes[pointIDBetweenCenterAndEdge * 3] = imageBytes[pointIDBetweenCenterAndEdge * 3 + 1] = imageBytes[pointIDBetweenCenterAndEdge * 3 + 2] = 0b1111110;

                    tFurtherColors[i + 5] = pixelInts[pointIDBetweenCenterAndEdge];
                }

                double avrgDistFromCenter = (thDists[0] + thDists[1] + thDists[2] + thDists[3]) / 4d * CAM_SETTINGS.PIECE_RECOGNITION_FIELD_SIZE_RADIUS_MULT;
                double KVal = 1d / (avrgDistFromCenter * Math.Sqrt(avrgDistFromCenter));

                //STATIC_MAIN_CAMERA_ANALYSER.ColorDifference

                double pieceDetVal = 0;


                for (int i = 0; i < tC; i++)
                {
                    int tID = tsquare[i];
                    //int tCol = pixelInts[tID];

                    double X = Vec2IntTupleDistance((txsum, tysum), (pixelColumnVals[tID], pixelRowVals[tID]));

                    if (activatedPixelsSaveState[tID])
                        pieceDetVal += Math.Clamp(1 / Math.Sqrt(X) - KVal * X, 0d, 1.5d); // 1 / sqrt(X) - 0.01X -> y = [0; 0.7]

                    //Math.Exp(X - 22)

                    //pieceDetVal += Math.Clamp(Math.Exp(-0.125 * Vec2IntTupleDistance((txsum, tysum), (pixelColumnVals[tID], pixelRowVals[tID])) - 0.4d), 0d, 0.7d);

                    //pieceDetVal += 0.01d;


                    //pieceDetVal += Math.Clamp(Math.Exp(-0.125 * Vec2IntTupleDistance((txsum, tysum), (pixelColumnVals[tID], pixelRowVals[tID])) - 0.4d), 0d, 0.4d);

                    //pieceDetVal += Math.Clamp(1d / (0.2 * Vec2IntTupleDistance((txsum, tysum), (pixelColumnVals[tID], pixelRowVals[tID])) + 1d), 0d, 0.2d);

                    //if (tID - width > -1) pieceDetVal += Math.Pow(STATIC_MAIN_CAMERA_ANALYSER.GetActualColorDifferenceVal(pixelInts[tID - width], tCol), 2);
                    //if (tID + width < pixelCount) pieceDetVal += Math.Pow(STATIC_MAIN_CAMERA_ANALYSER.GetActualColorDifferenceVal(pixelInts[tID + width], tCol), 2);
                    //if ((tID + 1) % width == 0) pieceDetVal += Math.Pow(STATIC_MAIN_CAMERA_ANALYSER.GetActualColorDifferenceVal(pixelInts[tID + 1], tCol), 2);
                    //if (tID % width == 0) pieceDetVal += Math.Pow(STATIC_MAIN_CAMERA_ANALYSER.GetActualColorDifferenceVal(pixelInts[tID - 1], tCol), 2);

                    //for (int c = 0; c < 9; c++)
                    //{
                    //    //if (c == 4) continue;
                    //    if (STATIC_MAIN_CAMERA_ANALYSER.GetActualColorDifferenceVal(tFurtherColors[c], tCol) < 3f)
                    //    {
                    //        int tidx = tID * 3;
                    //        imageBytes[tidx] = imageBytes[tidx + 1] = imageBytes[tidx + 2] = 0b11;
                    //        break;
                    //    }
                    //}
                    //if (STATIC_MAIN_CAMERA_ANALYSER.GetActualColorDifferenceVal(60 << 16 | 60 << 8 | 60, tCol) < 25f)
                    //{
                    //    int tidx = tID * 3;
                    //    imageBytes[tidx] = imageBytes[tidx + 1] = imageBytes[tidx + 2] = 0b11;
                    //}
                }

                if (pieceDetVal > CAM_SETTINGS.MIN_PIECE_RECOGNITION_DETERMINANT)
                {
                    for (int i = 0; i < tC; i++)
                    {
                        int tID = tsquare[i];
                        activatedPixelsSaveState2[tID] = true;
                        int tidx = tID * 3;
                        pixelDetVal[tID] = pieceDetVal;
                        imageBytes[tidx] = imageBytes[tidx + 1] = imageBytes[tidx + 2] = 0b111111;
                    }
                }

                dets[s] = pieceDetVal;
            }

            Array.Sort(dets);

            double[] detthresholds = new double[CAM_SETTINGS.PIECE_RECOGNITION_SOLUTIONS - 1];
            int kkkk = -1;

            for (int i = 0; i < fsqC; i++)
            {
                Console.Write((int)(dets[i] * 100) / 100d + ">");

                if (kkkk != -1 && kkkk + 1 < CAM_SETTINGS.PIECE_RECOGNITION_SOLUTIONS)
                {
                    detthresholds[kkkk] = dets[i];
                    kkkk++;
                }
                else if (kkkk == -1 && dets[i] > CAM_SETTINGS.MIN_PIECE_RECOGNITION_DETERMINANT) {
                    kkkk = 1;
                    detthresholds[0] = dets[i];
                }
            }
            Console.WriteLine();

            Marshal.Copy(imageBytes, 0, scan0, imageBytes.Length);
            img.Save(pathToResultPNGs[14], ImageFormat.Png);

            #endregion

            #region | STEP 8 - Square Indexing |

            Console.WriteLine("STEP 7 - Piece Recognition   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Restart();

            int[] finalVertLineStartPoses = new int[9], finalHorLineStartPoses = new int[9];

            for (int i = 0; i < 9; i++)
            {
                int tC1 = finalVertLinePoses[i].Count, tC2 = finalHorLinePoses[i].Count;

                for (int k = 0; k < tC1; k++)
                {
                    int tID = finalVertLinePoses[i][k];
                    if (pixelRowVals[tID] == 0)
                    {
                        finalVertLineStartPoses[i] = tID;
                        break;
                    }
                }

                for (int k = 0; k < tC2; k++)
                {
                    int tID = finalHorLinePoses[i][k];
                    if (pixelColumnVals[tID] == 0)
                    {
                        finalHorLineStartPoses[i] = pixelRowVals[tID];
                        break;
                    }
                }
            }

            Array.Sort(finalVertLineStartPoses, finalVertLinePoses);
            Array.Sort(finalHorLineStartPoses, finalHorLinePoses);

            int[][] OPys = new int[9][];
            ulong finalBoard = 0ul;
            ulong[] finalBoardChanges = new ulong[CAM_SETTINGS.PIECE_RECOGNITION_SOLUTIONS];

            for (int i = 0; i < 8; i++)
            {
                OPys[i] = new int[9];
                int tC1 = finalVertLinePoses[i].Count, tA = 0;
                for (int k = 0; k < tC1; k++)
                {
                    int tID = finalVertLinePoses[i][k];
                    if (Int2TupleArrayContains(OP_POINTS, (pixelColumnVals[tID], pixelRowVals[tID])))
                    {
                        OPys[i][tA++] = tID;
                    }
                }

                Array.Sort(OPys[i]);

                for (int j = 0; j < 8; j++)
                {
                    int tID = OPys[i][j] + 15 * width + 15;

                    if (activatedPixelsSaveState2[tID])
                    {
                        finalBoard = ULONG_OPERATIONS.SetBitToOne(finalBoard, i * 8 + j);

                        for (int p = 0; p < CAM_SETTINGS.PIECE_RECOGNITION_SOLUTIONS - 1; p++)
                        {
                            if (pixelDetVal[tID] <= detthresholds[p])
                            {
                                finalBoardChanges[p + 1] = ULONG_OPERATIONS.SetBitToOne(finalBoardChanges[p], i * 8 + j);
                            }
                        }
                    }
                }
            }

            img.Dispose();

            Console.WriteLine("STEP 8 - Square Indexing   [ " + sw.ElapsedMilliseconds + "ms ]");
            sw.Stop();

            Console.WriteLine("\n");
            
            for (int sol = 0; sol < CAM_SETTINGS.PIECE_RECOGNITION_SOLUTIONS; sol++)
            {
                STATIC_MAIN_CAMERA_ANALYSER.RESULT.Add(TurnPerspective(finalBoard ^ finalBoardChanges[sol]));
            }

            //STATIC_MAIN_CAMERA_ANALYSER.RESULT = finalBoard;

            #endregion
        }

        public static ulong TurnPerspective(ulong pULInp)
        {
            switch (ARDUINO_GAME_SETTINGS.CAM_LINE)
            {
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h1_h8:
                    return pULInp;
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.a1_h1:
                    return ULONG_OPERATIONS.ReverseByteOrder(ULONG_OPERATIONS.FlipBoard90Degress(pULInp));
                case ARDUINO_GAME_SETTINGS.CAMERA_BOTTOM_LINE.h8_a8:
                    return ULONG_OPERATIONS.FlipBoardHorizontally(ULONG_OPERATIONS.FlipBoard90Degress(pULInp));
                default:
                    return ULONG_OPERATIONS.ReverseByteOrder(ULONG_OPERATIONS.FlipBoardHorizontally(pULInp));
            }
        }

        #region | SPECIFIC IMAGE ANALYSIS FUNCTIONS |

        private void RecursiveCleanUp(ref List<int> tL, ref int tempLowestX, ref int tempHighestX, ref int tempLowestY, ref int tempHighestY, int pPix)
        {
            recursiveLimits.Clear();
            RecursivePixelFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix, 0);
            do
            {
                int tC = recursiveLimits.Count;

                int[] limitArray = new int[tC];
                for (int i = 0; i < tC; i++)
                    limitArray[i] = recursiveLimits[i];

                recursiveLimits.Clear();

                for (int i = 0; i < tC; i++)
                {
                    RecursivePixelFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, limitArray[i], 0);
                }

            } while (recursiveLimits.Count != 0);
        }

        private void RecursivePixelFunction(ref List<int> tL, ref int tempLowestX, ref int tempHighestX, ref int tempLowestY, ref int tempHighestY, int pPix, int pDepth)
        {
            if ((lookedThroughPixels[pPix] && pDepth != 0) || pDepth > 8000)
            {
                if (pDepth > 8000)
                {
                    recursiveLimits.Add(pPix);
                }
                return;
            }

            tL.Add(pPix);
            lookedThroughPixels[pPix] = true;

            int tRow = (pPix - pPix % width) / width,
                    tColumn = pPix % width;

            if (tRow > tempHighestX) tempHighestX = tRow;
            else if (tRow < tempLowestX) tempLowestX = tRow;
            if (tColumn > tempHighestY) tempHighestY = tColumn;
            else if (tColumn < tempLowestY) tempLowestY = tColumn;

            if (pPix + 1 < pixelCount && activatedPixels[pPix + 1] && (pPix + 1) % width != 0)
                RecursivePixelFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix + 1, pDepth + 1);
            if (pPix - 1 > -1 && activatedPixels[pPix - 1] && pPix % width != 0)
                RecursivePixelFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix - 1, pDepth + 1);
            if (pPix + width < pixelCount && activatedPixels[pPix + width])
                RecursivePixelFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix + width, pDepth + 1);
            if (pPix - width > -1 && activatedPixels[pPix - width])
                RecursivePixelFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix - width, pDepth + 1);
        }

        private bool FillEdgeReplacementDetermination(ref List<int> tL, int pPix)
        {
            int tRow = pixelRowVals[pPix], tColumn = pixelColumnVals[pPix];

            int tLowestX = tColumn, tHighestX = tColumn, tLowestY = tRow, tHighestY = tRow;

            recursiveLimits.Clear();

            RecursiveFillFunction(ref tL, ref tLowestX, ref tHighestX, ref tLowestY, ref tHighestY, pPix, 0);
            do
            {
                int tC = recursiveLimits.Count;

                int[] limitArray = new int[tC];
                for (int i = 0; i < tC; i++)
                    limitArray[i] = recursiveLimits[i];

                recursiveLimits.Clear();

                for (int i = 0; i < tC; i++)
                {
                    RecursiveFillFunction(ref tL, ref tLowestX, ref tHighestX, ref tLowestY, ref tHighestY, limitArray[i], 0);
                }

            } while (recursiveLimits.Count != 0);

            int edgeTileCount = 0;
            for (int i = 0; i < tL.Count; i++)
            {
                int tX = pixelColumnVals[tL[i]], tY = pixelRowVals[tL[i]];
                if (tX == 0 || tY == 0) edgeTileCount++;
                if (tX + 1 == width || tY + 1 == height) edgeTileCount++;
            }

            return edgeTileCount > CAM_SETTINGS.SQUARE_MAX_EDGE_PIXEL;
        }

        private bool FillReplacementDetermination(ref List<int> tL, int pPix)
        {
            int tRow = pixelRowVals[pPix], tColumn = pixelColumnVals[pPix];

            int tLowestX = tColumn, tHighestX = tColumn, tLowestY = tRow, tHighestY = tRow;

            recursiveLimits.Clear();

            RecursiveFillFunction(ref tL, ref tLowestX, ref tHighestX, ref tLowestY, ref tHighestY, pPix, 0);
            do
            {
                int tC = recursiveLimits.Count;

                int[] limitArray = new int[tC];
                for (int i = 0; i < tC; i++)
                    limitArray[i] = recursiveLimits[i];

                recursiveLimits.Clear();

                for (int i = 0; i < tC; i++)
                {
                    RecursiveFillFunction(ref tL, ref tLowestX, ref tHighestX, ref tLowestY, ref tHighestY, limitArray[i], 0);
                }

            } while (recursiveLimits.Count != 0);

            int xAsp = tHighestX - tLowestX, yAsp = tHighestY - tLowestY;
            int tSize = xAsp * yAsp;

            if (xAsp == 0 || yAsp == 0) return true;

            double aspectRatio = (double)yAsp / xAsp;


            int edgeTileCount = 0;
            double sumX = 0, sumY = 0;
            for (int i = 0; i < tL.Count; i++)
            {
                int tX = pixelColumnVals[tL[i]], tY = pixelRowVals[tL[i]];
                if (tX == 0 || tY == 0) edgeTileCount++;
                if (tX + 1 == width || tY + 1 == height) edgeTileCount++;
                sumX += tX;
                sumY += tY;
            }
            sumX /= tL.Count;
            sumY /= tL.Count;
            if (edgeTileCount > CAM_SETTINGS.SQUARE_MAX_EDGE_PIXEL) return true;

            double[] distsToMidOfBigSquare = new double[4];
            (int, int)[] tpoints = new (int, int)[4];

            for (int i = 0; i < tL.Count; i++)
            {
                int tX = pixelColumnVals[tL[i]], tY = pixelRowVals[tL[i]];
                int tV;

                if (tX > sumX)
                {
                    if (tY > sumY) tV = 0;
                    else tV = 1;
                }
                else
                {
                    if (tY > sumY) tV = 3;
                    else tV = 2;
                }

                double d;
                if ((d = Vec2IntTupleDistance(((int)sumX, (int)sumY), (tX, tY))) > distsToMidOfBigSquare[tV])
                {
                    distsToMidOfBigSquare[tV] = d;
                    tpoints[tV] = (tX, tY);
                }
            }

            (double, double) tRightNVec = Normalize2IntTuple(GetVecBetweenPoints(tpoints[0], tpoints[1]));
            (double, double) tLeftNVec = Normalize2IntTuple(GetVecBetweenPoints(tpoints[2], tpoints[3]));
            (double, double) tUpNVec = Normalize2IntTuple(GetVecBetweenPoints(tpoints[2], tpoints[1]));
            (double, double) tBottomNVec = Normalize2IntTuple(GetVecBetweenPoints(tpoints[3], tpoints[0]));

            double orthogonality = ScalarProduct(tRightNVec, tUpNVec) + ScalarProduct(tRightNVec, tBottomNVec) +
            ScalarProduct(tLeftNVec, tUpNVec) + ScalarProduct(tLeftNVec, tBottomNVec);

            if (
                ScalarProduct(tRightNVec, tUpNVec) > CAM_SETTINGS.SQUARE_MAX_INDIVIDUAL_SCALARPRODUCT ||
                ScalarProduct(tRightNVec, tBottomNVec) > CAM_SETTINGS.SQUARE_MAX_INDIVIDUAL_SCALARPRODUCT ||
                ScalarProduct(tLeftNVec, tUpNVec) > CAM_SETTINGS.SQUARE_MAX_INDIVIDUAL_SCALARPRODUCT ||
                ScalarProduct(tLeftNVec, tBottomNVec) > CAM_SETTINGS.SQUARE_MAX_INDIVIDUAL_SCALARPRODUCT)
                return true;

            return ((double)tL.Count / tSize) < CAM_SETTINGS.SQUARYNESS_DETERMINANT
                || tL.Count < CAM_SETTINGS.MIN_FILL_OBJECT_SIZE
                || aspectRatio > CAM_SETTINGS.SQUARE_MAX_ASPECT_RATIO_DISTORTION
                || aspectRatio < CAM_SETTINGS.SQUARE_MIN_ASPECT_RATIO_DISTORTION
                || orthogonality > CAM_SETTINGS.SQUARE_MAX_SUM_OF_SCALARPRODUCT;
        }

        private void RecursiveFillFunction(ref List<int> tL, ref int tempLowestX, ref int tempHighestX, ref int tempLowestY, ref int tempHighestY, int pPix, int pDepth)
        {
            if ((lookedThroughPixels[pPix] && pDepth != 0) || pDepth > 8000)
            {
                if (pDepth > 8000)
                {
                    recursiveLimits.Add(pPix);
                }
                return;
            }

            tL.Add(pPix);
            lookedThroughPixels[pPix] = true;

            int tRow = pixelRowVals[pPix], tColumn = pixelColumnVals[pPix];

            if (tRow > tempHighestY) tempHighestY = tRow;
            else if (tRow < tempLowestY) tempLowestY = tRow;
            if (tColumn > tempHighestX) tempHighestX = tColumn;
            else if (tColumn < tempLowestX) tempLowestX = tColumn;

            if (pPix + 1 < pixelCount && !activatedPixels[pPix + 1] && (pPix + 1) % width != 0)
                RecursiveFillFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix + 1, pDepth + 1);
            if (pPix - 1 > -1 && !activatedPixels[pPix - 1] && pPix % width != 0)
                RecursiveFillFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix - 1, pDepth + 1);
            if (pPix + width < pixelCount && !activatedPixels[pPix + width])
                RecursiveFillFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix + width, pDepth + 1);
            if (pPix - width > -1 && !activatedPixels[pPix - width])
                RecursiveFillFunction(ref tL, ref tempLowestX, ref tempHighestX, ref tempLowestY, ref tempHighestY, pPix - width, pDepth + 1);
        }

        private int VerticalLineDetection(int pPix, ref List<int> tL)
        {
            int tRow = pixelRowVals[pPix], tColumn = pixelColumnVals[pPix];
            RecursiveLineDetection(pPix, 0, true, false, ref tL);

            int tC = tL.Count, tHRow = tRow, tHColumn = tColumn;

            for (int i = 0; i < tC; i++)
            {
                int t_ti_px = tL[i], t_ti_Row = (t_ti_px - t_ti_px % width) / width, t_ti_Column = t_ti_px % width;
                if (t_ti_Row > tHRow || (t_ti_Row == tHRow && t_ti_Column < tHColumn))
                {
                    tHRow = t_ti_Row;
                    tHColumn = t_ti_Column;
                }
            }

            return tHRow - tRow;
        }

        private void RecursiveLineDetection(int pPix, int pDepth, bool pRight, bool pNoBlock, ref List<int> tL)
        {
            if (pDepth > 8000 || lookedThroughPixels[pPix] || !activatedPixels[pPix]) return;

            tL.Add(pPix);
            lookedThroughPixels[pPix] = true;

            if (pPix + width < pixelCount)
                RecursiveLineDetection(pPix + width, pDepth + 1, pRight, true, ref tL);
            if (pRight && pNoBlock && pPix + 1 < pixelCount && (pPix + 1) % width != 0)
                RecursiveLineDetection(pPix + 1, pDepth + 1, pRight, false, ref tL);
            if (!pRight && pNoBlock && pPix - 1 > -1 && pPix % width != 0)
                RecursiveLineDetection(pPix - 1, pDepth + 1, pRight, false, ref tL);
        }

        private (double, double) GetLinearFunctionFromTwoPoints((int, int) pP1, (int, int) pP2)
        {
            double tM;
            if (pP1.Item1 - pP2.Item1 == 0) tM = 1000d;
            else tM = (pP1.Item2 - pP2.Item2) / (pP1.Item1 - pP2.Item1);
            return (tM, pP2.Item2 - tM * pP2.Item1);
        }

        private double SetFunctionToVal((double, double) pFunc, double pVal)
        {
            return pVal / pFunc.Item1 - pFunc.Item2;
        }


        List<int> horlineids = new List<int>();
        List<byte> horlineidstrengths = new List<byte>();
        List<int> verlineids = new List<int>();
        List<byte> verlineidstrengths = new List<byte>();

        private List<(int, int)> GetHorizontalLineLinearRegressionOptimizorPoints(double pLineXe1VecY, int startYPos, bool[] pActivatedPixelSaveState)
        {
            int tID = startYPos * width;
            double yProgr = 0d;

            //if (horsqstartpos[startYPos]) pLineXe1VecY = horsqstartposd[startYPos];

            List<(int, int)> rPoints = new List<(int, int)>();

            for (int i = 0; i < width; i++)
            {
                if (yProgr >= 1d)
                {
                    yProgr -= 1d;
                    tID += width;
                }
                else if (yProgr <= -1d)
                {
                    yProgr += 1d;
                    tID -= width;
                }

                if (tID > pixelCount) break;


                for (int d = 0; d <= CAM_SETTINGS.HORIZONTALLINE_MAX_LINREG_OPTIMIZATION_DIST; d++)
                {
                    int idOp1 = tID + width * d;
                    int idOp2 = tID - width * d;

                    if (idOp1 < pixelCount && pActivatedPixelSaveState[idOp1])
                    {
                        rPoints.Add((pixelColumnVals[idOp1], pixelRowVals[idOp1]));
                        break;
                    }
                    else if (idOp2 > -1 && pActivatedPixelSaveState[idOp2])
                    {
                        rPoints.Add((pixelColumnVals[idOp2], pixelRowVals[idOp2]));
                        break;
                    }
                }


                tID++;
                yProgr += pLineXe1VecY;
            }

            //Console.WriteLine(rPoints.Count);

            return rPoints;
        }

        private List<int> GetHorizontalLine(double pLineXe1VecY, int startYPos)
        {
            int tID = startYPos * width;
            double yProgr = 0d;
            List<int> ids = new List<int>();

            for (int i = 0; i < width; i++)
            {
                if (yProgr >= 1d)
                {
                    yProgr -= 1d;
                    tID += width;
                }
                else if (yProgr <= -1d)
                {
                    yProgr += 1d;
                    tID -= width;
                }

                if (tID > pixelCount) break;

                ids.Add(tID);

                tID++;
                yProgr += pLineXe1VecY;
            }

            return ids;
        }

        private int GetStartYPosOfHorizontalLine(double pLineXe1VecY, (int, int) pPos)
        {
            int tID = pPos.Item1 + pPos.Item2 * width;
            double xProgr = 0d;

            for (int i = 0; i < pPos.Item1; i++)
            {
                if (xProgr >= 1d)
                {
                    xProgr -= 1d;
                    tID += width;
                    if (tID >= pixelCount) break;
                }
                else if (xProgr <= -1d)
                {
                    xProgr += 1d;
                    tID -= width;
                    if (tID < 0) break;
                }

                if (tID < 0 || pixelColumnVals[tID] == 0) break; // DIESE LINE GRRRRRRRRRRRRRRRRRRRRRRRRRRRRRRR [tID]!!

                tID--;
                xProgr -= pLineXe1VecY;
            }
            return pixelRowVals[tID < 0 ? 0 : tID];
        }

        private int GetStartXPosOfVerticalLine(double pLineYe1VecX, (int, int) pPos)
        {
            int tID = pPos.Item1 + pPos.Item2 * width;
            double xProgr = 0d;

            for (int i = 0; i < pPos.Item2; i++)
            {
                if (xProgr >= 1d)
                {
                    xProgr -= 1d;
                    tID++;
                    if (tID >= pixelCount || pixelColumnVals[tID] == 0) break;
                }
                else if (xProgr <= -1d)
                {
                    xProgr += 1d;
                    if (pixelColumnVals[tID] == 0) break;
                    tID--;
                }

                if (tID > pixelCount) break;

                tID -= width;
                xProgr -= pLineYe1VecX;
            }

            return tID;
        }

        private List<int> GetVerticalLine(double pLineYe1VecX, int startXPos)
        {
            int tID = startXPos;
            double xProgr = 0d;
            List<int> ids = new List<int>();

            for (int i = 0; i < height; i++)
            {
                if (xProgr >= 1d)
                {
                    xProgr -= 1d;
                    tID++;
                    if (tID >= pixelCount || pixelColumnVals[tID] == 0) break;
                }
                else if (xProgr <= -1d)
                {
                    xProgr += 1d;
                    if (pixelColumnVals[tID] == 0) break;
                    tID--;
                }

                if (tID >= pixelCount) break;

                ids.Add(tID);

                tID += width;
                xProgr += pLineYe1VecX;
            }

            return ids;
        }

        bool[] horhorhor = new bool[1000];

        private double GetFilledPrecentageOfHorizontalLine(double pLineXe1VecY, int startYPos)
        {
            int tID = startYPos * width, tC = 0, tC2 = 0;
            double yProgr = 0d;
            List<int> ids = new List<int>();

            if (horsqstartpos[startYPos]) pLineXe1VecY = horsqstartposd[startYPos];

            for (int i = 0; i < width; i++)
            {
                tC2++;
                if (yProgr >= 1d)
                {
                    yProgr -= 1d;
                    tID += width;
                    if (tID >= pixelCount) break;
                }
                else if (yProgr <= -1d)
                {
                    yProgr += 1d;
                    tID -= width;
                    if (tID < 0) break;
                }

                if (tID >= pixelCount) break;

                if (activatedPixels[tID])
                {
                    tC++;
                }

                ids.Add(tID);

                tID++;
                yProgr += pLineXe1VecY;
            }

            double tval = tC / (double)tC2;

            if (horsqstartpos[startYPos]) tval += CAM_SETTINGS.SECURE_SQUARE_LINE_DETERMINATION_BOOST_ONLY_HORIZONTAL;

            //bool bbbb = false;
            //for (int j = -15; j < 16; j++)
            //{
            //    if (startYPos + j < 0) continue;
            //    if (horhorhor[startYPos + j])
            //    {
            //        bbbb = true;
            //        break;
            //    }
            //}

            if (tval > 0.02)
            {
                horhorhor[startYPos] = true;
                horlineids.AddRange(ids);
                byte tbrightness = (byte)(Math.Clamp(255 * tval + 50, 0, 255));
                for (int i = 0; i < ids.Count; i++)
                {
                    horlineidstrengths.Add(tbrightness);
                }
            }

            return tval;
        }

        private double GetFilledPrecentageOfVerticalLine(double pLineYe1VecX, int startXPos)
        {
            int tID = startXPos, tC = 0, tC2 = 0;
            double xProgr = 0d;
            List<int> ids = new List<int>();

            for (int i = 0; i < height; i++)
            {
                tC2++;
                if (xProgr >= 1d)
                {
                    xProgr -= 1d;
                    tID++;
                    if (tID >= pixelCount || pixelColumnVals[tID] == 0) break;
                }
                else if (xProgr <= -1d)
                {
                    xProgr += 1d;
                    if (pixelColumnVals[tID] == 0) break;
                    tID--;
                }

                if (tID >= pixelCount) break;

                if (activatedPixels[tID])
                {
                    tC++;
                }

                ids.Add(tID);

                tID += width;
                xProgr += pLineYe1VecX;
            }

            double tval = tC / (double)tC2;

            if (versqstartpos[startXPos]) tval += CAM_SETTINGS.SECURE_SQUARE_LINE_DETERMINATION_BOOST;

            if (tval > 0.02)
            {
                verlineids.AddRange(ids);
                byte tbrightness = (byte)(Math.Clamp(255 * tval + 50, 0, 255));
                for (int i = 0; i < ids.Count; i++)
                {
                    verlineidstrengths.Add(tbrightness);
                }
            }

            return tval;
        }

        #endregion

        #region | GENERAL UTILITY |

        private (int, int) GetVecBetweenPoints((int, int) A, (int, int) B)
        {
            return (A.Item1 - B.Item1, A.Item2 - B.Item2);
        }

        private double AreaOfTriangleCombinations((int, int) A, (int, int) B, (int, int) C, (int, int) D, (int, int) P)
        {
            return AreaOfTriangle(A, D, P) + AreaOfTriangle(C, D, P) + AreaOfTriangle(C, B, P) + AreaOfTriangle(B, A, P);
        }

        private double AreaOfTriangle((int, int) A, (int, int) B, (int, int) C)
        {
            return Math.Abs((B.Item1 * A.Item2 - A.Item1 * B.Item2)
                            + (C.Item1 * B.Item2 - B.Item1 * C.Item2)
                            + (A.Item1 * C.Item2 - C.Item1 * A.Item2)) / 2;
        }

        private double Vec2IntTupleDistance((int, int) pt1, (int, int) pt2)
        {
            double d1 = pt2.Item1 - pt1.Item1, d2 = pt2.Item2 - pt1.Item2;

            return Math.Sqrt(d1 * d1 + d2 * d2);
        }

        private int EfficientAlternativeToVec2IntTupleDistance((int, int) pt1, (int, int) pt2)
        {
            int i1 = pt2.Item1 - pt1.Item1, i2 = pt2.Item2 - pt1.Item2;
            return i1 * i1 + i2 * i2;
        }

        private bool Int2TupleArrayContains((int, int)[] pArr, (int, int) pKey)
        {
            int tL = pArr.Length;
            for (int i = 0; i < tL; i++)
            {
                if (pArr[i] == pKey) return true;
            }
            return false;
        }

        private bool IntListContains(List<int> pList, int pKey)
        {
            int tL = pList.Count;
            for (int i = 0; i < tL; i++)
            {
                if (pList[i] == pKey) return true;
            }
            return false;
        }

        private (int, int) Round2DoubleTuple((double, double) pTuple)
        {
            return ((int)pTuple.Item1, (int)pTuple.Item2);
        }

        private (double, double) Normalize2IntTuple((int, int) pTuple)
        {
            double len = Math.Sqrt(pTuple.Item1 * pTuple.Item1 + pTuple.Item2 * pTuple.Item2);
            return (pTuple.Item1 / len, pTuple.Item2 / len);
        }

        private (double, double) Scale2DoubleTupleXTo((double, double) pTuple, double pScale)
        {
            double mult = pScale / pTuple.Item1;

            return (pTuple.Item1 * mult, pTuple.Item2 * mult);
        }

        private (double, double) Scale2DoubleTupleYTo((double, double) pTuple, double pScale)
        {
            double mult = pScale / pTuple.Item2;

            return (pTuple.Item1 * mult, pTuple.Item2 * mult);
        }

        private (int, int) LineValuesBetweenTwoPoints((int, int) pPoint1, (int, int) pPoint2)
        {
            return (pPoint1.Item1 - pPoint2.Item1, pPoint1.Item2 - pPoint2.Item2);
        }

        private double GetAverage(int[] pArr)
        {
            int tL = pArr.Length;
            double sum = 0;
            for (int i = 0; i < tL; i++) { sum += pArr[i]; }
            return sum / tL;
        }
        private double GetAverage(double[] pArr)
        {
            int tL = pArr.Length;
            double sum = 0;
            for (int i = 0; i < tL; i++) { sum += pArr[i]; }
            return sum / tL;
        }

        private double GetStandardDeviation(int[] pArr)
        {
            int tL = pArr.Length;
            double avrg = GetAverage(pArr), itres = 0, inversL = 1d / tL;
            for (int i = 0; i < tL; i++)
            {
                double d = avrg - pArr[i];
                itres += d * d * inversL;
            }
            return Math.Sqrt(itres);
        }
        private double GetStandardDeviation(double[] pArr)
        {
            int tL = pArr.Length;
            double avrg = GetAverage(pArr), itres = 0, inversL = 1d / tL;
            for (int i = 0; i < tL; i++)
            {
                double d = avrg - pArr[i];
                itres += d * d * inversL;
            }
            return Math.Sqrt(itres);
        }


        private double GetCovariance(int[] pArrXs, int[] pArrVals)
        {
            int tL = pArrVals.Length;
            double avrgVals = GetAverage(pArrVals), avrgXs = GetAverage(pArrXs);
            double difsum = 0d;
            for (int i = 0; i < tL; i++)
                difsum += (avrgVals - pArrVals[i]) * (avrgXs - pArrXs[i]);

            return difsum / (tL - 1d);
        }

        private double GetPearsonCorrelation(int[] pArrXs, int[] pArrVals)
        {
            int tL = pArrVals.Length;
            double avrgVals = GetAverage(pArrVals), avrgXs = GetAverage(pArrXs);
            double sqsumX = 0d, sqsumV = 0d, multsum = 0d;
            for (int i = 0; i < tL; i++)
            {
                sqsumV += pArrVals[i] * pArrVals[i];
                sqsumX += pArrXs[i] * pArrXs[i];
                multsum += pArrXs[i] * pArrVals[i];
            }
            return (multsum - tL * avrgVals * avrgXs) / (Math.Sqrt(sqsumX - tL * avrgXs * avrgXs) * Math.Sqrt(sqsumV - tL * avrgVals * avrgVals));
        }
        private double GetPearsonCorrelation(double[] pArrXs, double[] pArrVals)
        {
            int tL = pArrVals.Length;
            double avrgVals = GetAverage(pArrVals), avrgXs = GetAverage(pArrXs);
            double sqsumX = 0d, sqsumV = 0d, multsum = 0d;
            for (int i = 0; i < tL; i++)
            {
                sqsumV += pArrVals[i] * pArrVals[i];
                sqsumX += pArrXs[i] * pArrXs[i];
                multsum += pArrXs[i] * pArrVals[i];
            }
            return (multsum - tL * avrgVals * avrgXs) / (Math.Sqrt(sqsumX - tL * avrgXs * avrgXs) * Math.Sqrt(sqsumV - tL * avrgVals * avrgVals));
        }

        private (double, double) LinearRegression(int[] pArrX, int[] pArrY)
        {
            double avrgY = GetAverage(pArrY), avrgX = GetAverage(pArrX);
            double sX = GetStandardDeviation(pArrX), sY = GetStandardDeviation(pArrY);
            double rxy = GetPearsonCorrelation(pArrX, pArrY);

            double b = -sY / sX * rxy * avrgX + avrgY;
            double m = sY / sX * rxy;

            return (m, b);
        }

        private (double, double) LinearRegression(double[] pArrX, double[] pArrY)
        {
            double avrgY = GetAverage(pArrY), avrgX = GetAverage(pArrX);
            double sX = GetStandardDeviation(pArrX), sY = GetStandardDeviation(pArrY);
            double rxy = GetPearsonCorrelation(pArrX, pArrY);

            double b = -sY / sX * rxy * avrgX + avrgY;
            double m = sY / sX * rxy;

            return (m, b);
        }

        private int GetValBiggestValJumpIdx(int[] pVals)
        {
            int tL = pVals.Length;
            int tLV = pVals[0]; //Pot Error
            int biggestChange = -1, tidx = -1;

            for (int i = 1; i < tL; i++)
            {
                if (pVals[i] - tLV > biggestChange)
                {
                    biggestChange = pVals[i] - tLV;
                    tidx = i;
                }
                tLV = pVals[i];
            }

            return tidx;
        }

        private double ScalarProduct((double, double) pVec1, (double, double) pVec2)
        {
            return pVec1.Item1 * pVec2.Item1 + pVec1.Item2 * pVec2.Item2;
        }

        private double VecLength((double, double) pVec)
        {
            return Math.Sqrt(pVec.Item1 * pVec.Item1 + pVec.Item2 * pVec.Item2);
        }

        private double AngleOfVec((double, double) pVec) // Die ist zwar aktuell nicht in Gebauch; aber glaub ich auch noch nicht ganz korrekt
        {
            double d = ScalarProduct(pVec, (1d, 0d)) / VecLength(pVec);

            if (pVec.Item1 > 0d)
            {
                if (pVec.Item2 > 0d) return Math.Acos(d);
                else return Math.Acos(d);
            }
            else
            {
                if (pVec.Item2 > 0d) return -Math.Acos(d);
                else return Math.Acos(d);
            }
        }

        private double GetAverage(int[] pArr, int pFrom, int pTo)
        {
            int tL = pArr.Length;
            double sum = 0;
            for (int i = pFrom; i < pTo; i++) { sum += pArr[i]; }
            return sum / (pTo - pFrom);
        }

        #endregion
    }

    public class PYTHONHANDLER
    {
        public PYTHONHANDLER(string programName)
        {
            Process cmdProcess = new Process();
            string strCMD = @"/C cd " + "\"" + CAM_SETTINGS.PROJECT_DIRECTORY + "\" && python " + programName + ".py";
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.WindowStyle = ProcessWindowStyle.Normal;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = strCMD;
            cmdProcess.StartInfo = startInfo;
            cmdProcess.Start();

            cmdProcess.WaitForExit();
        }
    }
}