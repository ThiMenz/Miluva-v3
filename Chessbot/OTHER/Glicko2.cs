namespace ChessBot
{
    public static class Glicko2
    {
        // Formeln von diesem PDF: http://www.glicko.net/glicko/glicko2.pdf

        private const double T = 0.5;

        public static void Test()
        {
            //GlickoEntity PLAYER = new GlickoEntity(1500, 200, 0.06);
            //
            //GlickoEntity[] OPPS = new GlickoEntity[3] {
            //    new GlickoEntity(1400, 30, 0.06),
            //    new GlickoEntity(1550, 100, 0.06),
            //    new GlickoEntity(1700, 300, 0.06)
            //};
            //
            //double[] tResults = new double[3] { 1, 0, 0 };
            //
            //CalculateRating(PLAYER, OPPS, tResults);
            //GlickoEntity Mila = new GlickoEntity("Mila");
            //GlickoEntity Luna = new GlickoEntity("Luna");
            //GlickoEntity Tino = new GlickoEntity("Tino");
            //GlickoEntity Filo = new GlickoEntity("Filo");
            //GlickoEntity Mark = new GlickoEntity("Mark");
            //
            //new GlickoGame(Mila, Luna, 0);
            //new GlickoGame(Tino, Filo, 1);
            //new GlickoGame(Mark, Filo, -1);
            //new GlickoGame(Mark, Luna, -1);
            //new GlickoGame(Mila, Tino, 1);
            //for (int i = 0; i < 1000; i++)
            //    new GlickoGame(Filo, Luna, -1);
            //
            //for (int i = 0; i < 1000; i++)
            //    new GlickoGame(Mark, Luna, -1);
            //new GlickoGame(Mila, Luna, 1);
            //new GlickoGame(Mark, Mila, 0);
            //UpdateAllEntities(new List<GlickoEntity>() { Mila, Luna, Tino, Filo, Mark });
        }

        public static void CalculateAllEntities(List<GlickoEntity> entityList)
        {
            foreach (GlickoEntity entity in entityList)
            {
                entity.CalculateNextEntityUpdate();
            }
        }

        public static void UpdateAllEntities(List<GlickoEntity> entityList)
        {
            foreach (GlickoEntity entity in entityList)
            {
                entity.UpdateEntity();
            }
        }

        #region | PURE GLICKO 2 RATING CALCULATIONS |

        public static void CalculateRating(GlickoEntity pEntityToGetRated, GlickoEntity[] pOpponents, double[] pResults)
        {
            int tOppCount = pOpponents.Length;

            double tElo = pEntityToGetRated.ELO;
            double tRD = pEntityToGetRated.RD;

            double tVol = pEntityToGetRated.VOL;

            // STEP 2
            double tGlicko2ScaleElo = ConvertToGlicko2ScaledElo(tElo);
            double tGlicko2ScaleRD = ConvertToGlicko2ScaledRD(tRD);

            if (tOppCount == 0)
            {
                tGlicko2ScaleRD = Math.Sqrt(tGlicko2ScaleRD * tGlicko2ScaleRD + tVol * tVol);
                pEntityToGetRated.NEXT_RD = ConvertToGlicko1ScaledRD(tGlicko2ScaleRD);
                return;
            }

            // STEP 3
            double tV = 0d;

            for (int i = 0; i < tOppCount; i++)
            {
                double d, d2;
                tV += Math.Pow(g(d = ConvertToGlicko2ScaledRD(pOpponents[i].RD)), 2)
                    * (d2 = E(tGlicko2ScaleElo, ConvertToGlicko2ScaledElo(pOpponents[i].ELO), d))
                    * (1 - d2);
            }

            tV = 1 / tV;

            //STEP 4
            double tDelta = 0d;
            for (int i = 0; i < tOppCount; i++)
            {
                double d;
                tDelta += g(d = ConvertToGlicko2ScaledRD(pOpponents[i].RD)) 
                    * (pResults[i] - E(tGlicko2ScaleElo, ConvertToGlicko2ScaledElo(pOpponents[i].ELO), d));
            }

            double tempDelta = tDelta;

            tDelta *= tV;

            //STEP 5.1
            double tA = Math.Log(tVol * tVol);
            double convergenceToleranceE = 0.000001;

            //STEP 5.2
            double ttA = tA, ttB;
            if (tDelta * tDelta > tGlicko2ScaleRD * tGlicko2ScaleRD + tV)
                ttB = Math.Log(tDelta * tDelta - tGlicko2ScaleRD * tGlicko2ScaleRD - tV);
            else
            {
                double tK_ts = 1;

                while (f(tA - tK_ts * T, tDelta, tGlicko2ScaleRD, tV, tA) < 0)
                    tK_ts++;

                ttB = tA - tK_ts * T;
            }

            //STEP 5.3
            double tFA = f(ttA, tDelta, tGlicko2ScaleRD, tV, tA);
            double tFB = f(ttB, tDelta, tGlicko2ScaleRD, tV, tA);

            //STEP 5.4
            while (Math.Abs(ttB - ttA) > convergenceToleranceE)
            {
                double ttC = ttA + (ttA - ttB) * tFA / (tFB - tFA);
                double tFC = f(ttC, tDelta, tGlicko2ScaleRD, tV, tA);
                if (tFC * tFB <= 0)
                {
                    ttA = ttB;
                    tFA = tFB;
                }
                else tFA /= 2;
                ttB = ttC;
                tFB = tFC;
            }

            //STEP 5.5
            double resVotality = Math.Exp(ttA / 2);

            //STEP 6
            double rdStar = Math.Sqrt(tGlicko2ScaleRD * tGlicko2ScaleRD + resVotality * resVotality);

            //STEP 7
            double resGlicko2RD = 1 / Math.Sqrt(1 / (rdStar * rdStar) + 1 / tV);
            double resGlicko2Elo = resGlicko2RD * resGlicko2RD * tempDelta + tGlicko2ScaleElo;

            //STEP 8
            pEntityToGetRated.NEXT_RD = ConvertToGlicko1ScaledRD(resGlicko2RD);
            pEntityToGetRated.NEXT_ELO = ConvertToGlicko1ScaledElo(resGlicko2Elo);
            pEntityToGetRated.NEXT_VOL = resVotality;
        }

        private static double f(double pX, double pDelta, double pRD, double pV, double pA)
        {
            double d = Math.Exp(pX);
            return 
                (d * (pDelta * pDelta - pRD * pRD - pV - d))
                / (2 * Math.Pow(pRD * pRD + pV + d, 2))
                - ((pX - pA) / (T * T));
        }

        private static double ConvertToGlicko1ScaledElo(double pVal)
        {
            return 173.7178 * pVal + 1500;
        }

        private static double ConvertToGlicko1ScaledRD(double pVal)
        {
            return 173.7178 * pVal;
        }

        private static double ConvertToGlicko2ScaledElo(double pVal)
        {
            return (pVal - 1500) / 173.7178;
        }

        private static double ConvertToGlicko2ScaledRD(double pVal)
        {
            return pVal / 173.7178;
        }

        private static double g(double pRD)
        {
            return 1 / Math.Sqrt(1 + 3 * pRD * pRD / (Math.PI * Math.PI));
        }

        private static double E(double pY, double pYJ, double pRDJ)
        {
            return 1 / (1 + Math.Exp(-g(pRDJ) * (pY - pYJ)));
        }

        #endregion
    }

    public class GlickoGame
    {
        public GlickoEntity entity1, entity2;
        public double result;

        public GlickoGame(GlickoEntity pE1, GlickoEntity pE2, double pRes)
        {
            entity1 = pE1;
            entity2 = pE2;
            result = pRes;

            entity1.glickoGames.Add(this);
            entity2.glickoGames.Add(this);
        }

        public (GlickoEntity, double) GetRelevantCalculationInformations(GlickoEntity pGE)
        {
            if (entity1 == pGE)
            {
                if (result == 1) return (entity2, 1);
                else if (result == -1) return (entity2, 0);
                else return (entity2, 0.5);
            }
            else
            {
                if (result == 1) return (entity1, 0);
                else if (result == -1) return (entity1, 1);
                else return (entity1, 0.5);
            }
        }
    }

    public class GlickoEntity
    {
        public string NAME;

        public double ELO = 1500, RD = 350, VOL = 0.06;
        public double NEXT_ELO, NEXT_RD, NEXT_VOL;

        public List<GlickoGame> glickoGames = new List<GlickoGame>();

        public GlickoEntity(string pName) { NAME = pName; }

        public GlickoEntity(string pName, double pElo, double pRD, double pVol)
        {
            NAME = pName;
            ELO = pElo;
            RD = pRD;
            VOL = pVol;
        }

        public void UpdateEntity()
        {
            Console.WriteLine();
            Console.WriteLine(" === " + NAME + " === ");
            Console.WriteLine("ELO: " + ELO + " -> " + NEXT_ELO);
            Console.WriteLine("RD: " + RD + " -> " + NEXT_RD);
            Console.WriteLine("Volatility: " + VOL + " -> " + NEXT_VOL);

            ELO = NEXT_ELO;
            RD = NEXT_RD;
            VOL = NEXT_VOL;
        }

        public void CalculateNextEntityUpdate()
        {
            int tL = glickoGames.Count;
            double[] tResults = new double[tL];
            GlickoEntity[] tOpps = new GlickoEntity[tL];
            for (int i = 0; i < tL; i++)
            {
                (GlickoEntity, double) tInfos = glickoGames[i].GetRelevantCalculationInformations(this);
                tResults[i] = tInfos.Item2;
                tOpps[i] = tInfos.Item1;
            }
            Glicko2.CalculateRating(this, tOpps, tResults);

            glickoGames.Clear();
        }
    }
}