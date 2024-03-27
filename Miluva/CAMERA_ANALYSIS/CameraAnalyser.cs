using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

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
        public const double COLOR_DIFFERENCE_THRESHOLD_FOR_PIECE_RECOGNITION = 5.5;

        // Eine Linie darf kein dickere Pixelbreite als diese haben
        public const int MAX_LINE_WIDTH = 4;

        // Prezentsatz der gefüllten Pixel, damit eine horizontale / vertikale Linie als sicher relevant anerkannt werden kann
        public const double HORIZONTALLINE_FULL_SECURE_DETERMINANT = .27;
        public const double VERTICALLINE_FULL_SECURE_DETERMINANT = .1;

        // Mindestabstand zwischen den einzelnen relevanten Linien; in diesem Intervall entscheidet sich der Algorithmus für die prozentual stärkste Linie
        public const int HORIZONTALLINE_MIN_DIST = 30;

        // Zwei Horizontale Linien dürfen nicht mehr als dieser Multiplikator des Abstands zur vorherigen Linie auseinander liegen 
        public const double MAX_HORIZONTALLINE_DIST_INCR = 1.5;

        // Vom oberen Rand der Aufnahme ausgehend darf keine Linie diese Distanz zum Rand des Bildes unterschreiten
        public const int VERTICALLINE_MIN_PIC_END_DIST = 10; //50

        // Maximale Abweichung in Pixeln, die der Abstand zur nächsten Linie im Vergleich zur vorherigen Linie haben darf
        public const int VERTICALLINE_MAX_DEVIATION_FROM_AVERAGE = 9;
        public const int HORIZONTALLINE_MAX_DEVIATION_FROM_AVERAGE = 15;

        // Bei Verfeinerung der Posierung der horizontalen Linien, dürfen nur Pixel mit diesem Y-Abstand als Wertpunkt für die lineare Regression genutzt werden
        public const int HORIZONTALLINE_MAX_LINREG_OPTIMIZATION_DIST = 16;

        // Zur Verfeinerung dieser Posierung müssen aber mindestens diese Anzahl an Wertpunkten gefunden werden
        public const int HORIZONTALLINE_MIN_LINREG_OPTIMIZATION_POINTS = 135;

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
        public const double SECURE_SQUARE_LINE_DETERMINATION_BOOST = 0.5;

        // Nach der Erstellung des Gitters der Felder; Maximale Anzahl an Randpixeln dieser Felder
        public const int SQUARE_MAX_EDGE_PIXEL = 0;

        // Maximale Grö0e eines Objekts zur Erkennung einer Störung (einzelne Noise Partikel zum Beispiel)
        public const int MAX_DISTORTION_SIZE = 30;

        // Sollte die "Main Area", also das Spielbrett mit der Pixelmasse oder mit dem Rechteckflächeninhalt erkannt werden
        public const bool MAIN_AREA_SELECTION_WITH_PIXELCOUNT = true;

        // Zusätze zur Main Area benötigen mindestens diese Größe (erneut Pixelmasse)
        public const int MAIN_AREA_ADDITION_MIN_SIZE = 700;

        // Quadrierter maximaler Abstand, den ein Pixel zu einem anderen der Main Area haben muss
        public const double MAIN_AREA_ADDITION_DIST_THRESHOLD = 100;

        // Threshold zur Erkennung der Figuren
        public const double MIN_PIECE_RECOGNITION_DETERMINANT = 23;

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

    public class CameraAnalyser
    {

    }

    public class PYTHONHANDLER
    {
        public PYTHONHANDLER(string programName)
        {
            Process cmdProcess = new Process();
            string strCMD = @"/C cd " + CAM_SETTINGS.PROJECT_DIRECTORY + " && python " + programName + ".py";
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
