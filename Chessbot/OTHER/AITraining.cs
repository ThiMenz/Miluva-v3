#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8618

namespace ChessBot
{
    #region | NEURAL NETWORKING |

    #region | CONVOLUTIONAL NEURAL NETWORKS |

    public class ConvolutionalNeuralNetwork2D
    {
        private CNN2DLayer[] layer;
        private int layerAmount;

        public ConvolutionalNeuralNetwork2D(CNN2DLayer[] pLayer)
        {
            layerAmount = pLayer.Length;
            layer = pLayer;
        }

        public double[] GetFlattenedOutputs(double[][,] pInputs)
        {
            for (int l = 0; l < layerAmount; l++)
                pInputs = layer[l].CalculateOutputs(pInputs);
            int outpDepth = pInputs.Length;
            List<double> rOutputs = new List<double>();
            for (int d = 0; d < outpDepth; d++)
            {
                double[,] tda = pInputs[d];
                int tx = tda.GetLength(0), ty = tda.GetLength(1);
                for (int x = 0; x < tx; x++)
                    for (int y = 0; y < ty; y++)
                        rOutputs.Add(tda[x, y]);
            }
            return rOutputs.ToArray();
        }
    }

    public class CNN2DLayer
    {
        private CNN2DKernel[] kernels;
        private CNN2DPooling[] poolings;

        private int kernelAmount;

        public CNN2DLayer(CNN2DKernel[] pKernels, CNN2DPooling[] pPoolings)
        {
            kernelAmount = pKernels.Length;
            kernels = pKernels;
            poolings = pPoolings;
        }

        public double[][,] CalculateOutputs(double[][,] pInputs)
        {
            int inpLen = pInputs.Length, a = 0;
            double[][,] rVals = new double[inpLen * kernelAmount][,];
            for (int i = 0; i < inpLen; i++)
            {
                double[,] tInputs = pInputs[i];
                int inpXLen = tInputs.GetLength(0), inpYLen = tInputs.GetLength(1);
                for (int k = 0; k < kernelAmount; k++)
                    rVals[a++] = poolings[i].Pooling(kernels[i].ApplyKernelOnInput(tInputs, inpXLen, inpYLen), inpXLen, inpYLen);
            }
            return rVals;
        }
    }

    public class CNN2DPooling
    {
        private int stride, poolingMethod;
        private double avrgFactor;

        public CNN2DPooling(int pStride, string pPoolingMethod)
        {
            stride = pStride;
            avrgFactor = 1d / (double)(stride * stride);
            string s = pPoolingMethod.ToLower();
            if (s == "max" || s == "maxpool" || s == "maxpooling") poolingMethod = 0;
            else if (s == "average" || s == "averagepool" || s == "averagepooling") poolingMethod = 1;
        }

        public double[,] Pooling(double[,] pMatrix2D, int pDim1, int pDim2)
        {
            if (poolingMethod == 0)
                return MaxPooling(pMatrix2D, pDim1, pDim2);
            else if (poolingMethod == 1)
                return AveragePooling(pMatrix2D, pDim1, pDim2);
            return null;
        }

        public double[,] AveragePooling(double[,] pMatrix2D, int pDim1, int pDim2) //avrgFactor is the derivative
        {
            double[,] rMatrix = new double[(pDim1 + pDim1 % stride) / stride, (pDim2 + pDim2 % stride) / stride];
            int a = 0, b = 0;
            for (int i = 0; i < pDim1; i += stride)
            {
                for (int j = 0; j < pDim2; j += stride)
                {
                    double tVal = 0d;
                    for (int x = 0; x < stride; x++)
                        for (int y = 0; y < stride; y++)
                            tVal += avrgFactor * pMatrix2D[i + x, j + y];
                    rMatrix[a, b++] = tVal;
                }
                a++;
            }
            return rMatrix;
        }

        public double[,] MaxPooling(double[,] pMatrix2D, int pDim1, int pDim2) //1 for the MaxVal is the derivative
        {
            double[,] rMatrix = new double[(pDim1 + pDim1 % stride) / stride, (pDim2 + pDim2 % stride) / stride];
            int a = 0, b = 0;
            for (int i = 0; i < pDim1; i += stride)
            {
                for (int j = 0; j < pDim2; j += stride)
                {
                    double maxVal = double.MinValue;
                    for (int x = 0; x < stride; x++)
                    {
                        for (int y = 0; y < stride; y++)
                        {
                            double d = pMatrix2D[i + x, j + y];
                            if (d > maxVal) maxVal = d;
                        }
                    }
                    rMatrix[a, b++] = maxVal;
                }
                a++;
            }
            return rMatrix;
        }
    }

    public class CNN2DKernel
    {
        private double[,] mirroredKernelValues;
        private Func<double, double> kernelFunction;
        private int stride, kernelXMaxVal, kernelXKVal, kernelYMaxVal, kernelYKVal;

        public CNN2DKernel(int pxMin, int pxMax, int pyMin, int pyMax, int pStride, Func<double, double> pKernelFunction)
        {
            int xS = pxMax - pxMin, yS = pyMax - pyMin;
            double d = 1d / (xS * yS);
            mirroredKernelValues = new double[xS, yS];
            for (int i = 0; i < xS; i++)
                for (int j = 0; j < yS; j++)
                    mirroredKernelValues[i, j] = d;
            kernelXKVal = -pxMin;
            kernelYKVal = -pyMin;
            kernelXMaxVal = pxMax + 1;
            kernelYMaxVal = pyMax + 1;
            kernelFunction = pKernelFunction;
            stride = pStride;
        }

        public CNN2DKernel(double[,] pKernelValues, int pxMin, int pxMax, int pyMin, int pyMax, int pStride, Func<double, double> pKernelFunction)
        {
            kernelXKVal = -pxMin;
            kernelYKVal = -pyMin;
            kernelXMaxVal = pxMax + 1;
            kernelYMaxVal = pyMax + 1;
            kernelFunction = pKernelFunction;
            stride = pStride;
            int xSize = pKernelValues.GetLength(0), ySize = pKernelValues.GetLength(1);
            mirroredKernelValues = new double[xSize, ySize];
            for (int x = 0; x < xSize; x++)
                for (int y = 0; y < ySize; y++)
                    mirroredKernelValues[xSize - x - 1, ySize - y - 1] = pKernelValues[x, y];
        }

        public double[,] ApplyKernelOnInput(double[,] pInputMatrix, int pImgDim1, int pImgDim2)
        {
            double[,] outputMatrix = new double[(pImgDim1 + pImgDim1 % stride) / stride, (pImgDim2 + pImgDim2 % stride) / stride];

            int aX = 0, aY = 0;
            for (int imgX = 0; imgX < pImgDim1; imgX += stride)
            {
                for (int imgY = 0; imgY < pImgDim2; imgY += stride)
                {
                    double d = 0;
                    for (int kX = -kernelXKVal; kX < kernelXMaxVal; kX++)
                    {
                        int ttx = imgX + kX;
                        if (ttx < 0 || ttx >= pImgDim1) continue;
                        for (int kY = -kernelYKVal; kY < kernelYMaxVal; kY++)
                        {
                            int tty = imgY + kY;
                            if (tty < 0 || tty >= pImgDim2) continue;
                            d += mirroredKernelValues[kX + kernelXKVal, kY + kernelYKVal] * pInputMatrix[ttx, tty];
                        }
                    }
                    outputMatrix[aX, aY++] = kernelFunction(d);
                }
                aX++;
                aY = 0;
            }

            return outputMatrix;
        }
    }

    #endregion

    #region    | ARTIFICIAL NEURAL NETWORKS |

    public class NeuralNetwork
    {
        private int layerAmount, outputAmount;
        private NeuralNetworkLayer outputLayer;
        public NeuralNetworkLayer[] layer { get; private set; }

        private Func<double, double> neuronFunction, neuronDerivativeFunction, diviationFunction, deviationDerivativeFunction, outputNeuronFunction, outputNeuronDerivativeFunction;

        public NeuralNetwork(Func<double, double> pNeuronFunction, Func<double, double> pNeuronDerivativeFunction, Func<double, double> pOutputNeuronFunction, Func<double, double> pOutputNeuronDerivativeFunction, Func<double, double> pDeviationFunction, Func<double, double> pDeviationDerivativeFunction, int pInputAmount, params int[] pLayersNodeAmount)
        {
            layer = new NeuralNetworkLayer[layerAmount = pLayersNodeAmount.Length];
            outputAmount = pLayersNodeAmount[layerAmount - 1];
            neuronFunction = pNeuronFunction;
            neuronDerivativeFunction = pNeuronDerivativeFunction;
            diviationFunction = pDeviationFunction;
            deviationDerivativeFunction = pDeviationDerivativeFunction;

            int prevLayerNodeAmount = pInputAmount;
            for (int l = 0; l < layerAmount - 1; l++)
                layer[l] = new NeuralNetworkLayer(prevLayerNodeAmount, prevLayerNodeAmount = pLayersNodeAmount[l], neuronFunction, neuronDerivativeFunction, diviationFunction, deviationDerivativeFunction);
            outputLayer = layer[layerAmount - 1] = new NeuralNetworkLayer(prevLayerNodeAmount, pLayersNodeAmount[layerAmount - 1], pOutputNeuronFunction, pOutputNeuronDerivativeFunction, diviationFunction, deviationDerivativeFunction);
        }

        public void AddAdditionalNeuronFunctionToOutputLayer(Func<double[], int, double[]> pFunc, Func<double[], int, double[]> pDerivativeFunc)
        {
            outputLayer.AddAdditionalNeuronFunction(pFunc, pDerivativeFunc);
        }

        private const double GrDescH = 0.00001d;
        public void RawGradientDescent(TrainingData[] pTrainingData, double pLearnRate)
        {
            double unchangedNNDeviation = CalculateDeviation(pTrainingData);
            int tLayerInpNodeAmount, tLayerOutpNodeAmount = layer[0].previousLayerNodeAmount;
            NeuralNetworkLayer tNNL;
            for (int l = 0; l < layerAmount; l++)
            {
                tLayerInpNodeAmount = tLayerOutpNodeAmount;
                tLayerOutpNodeAmount = (tNNL = layer[l]).thisLayerNodeAmount;
                for (int outputNode = 0; outputNode < tLayerOutpNodeAmount; outputNode++)
                {
                    for (int inputNode = 0; inputNode < tLayerInpNodeAmount; inputNode++)
                    {
                        tNNL.weights[inputNode, outputNode] += GrDescH;
                        double deltaDeviation = CalculateDeviation(pTrainingData) - unchangedNNDeviation;
                        tNNL.weights[inputNode, outputNode] -= GrDescH;
                        tNNL.deviationGradientWeights[inputNode, outputNode] = deltaDeviation / GrDescH;
                    }

                    tNNL.biases[outputNode] += GrDescH;
                    double biasDeltaDeviation = CalculateDeviation(pTrainingData) - unchangedNNDeviation;
                    tNNL.biases[outputNode] -= GrDescH;
                    tNNL.deviationGradientBiases[outputNode] = biasDeltaDeviation / GrDescH;
                }
            }
            ApplyAllDiviationGradients(pLearnRate);
        }

        public void GradientDescent(TrainingData[] pTrainingData, double pLearnRate, bool pLog)
        {
            int tTrainingBatchSize = pTrainingData.Length;
            //int pBatchOffset = miniBatchSize, pU = 0;
            //while (pU < tTrainingBatchSize)
            //{
            //    for (int d = pU; d < pBatchOffset; d++)
            //    {
            //        UpdateAllDeviationGradients(pTrainingData[d]);
            //    }
            //    ApplyAllDiviationGradients(pLearnRate / tTrainingBatchSize);
            //    if (pLog)
            //    {
            //        LogManager.LogANNGradients(layer);
            //    }
            //    ClearAllDeviationGradients();
            //    pBatchOffset += miniBatchSize;
            //    pU += miniBatchSize;
            //    if (pBatchOffset > tTrainingBatchSize) pBatchOffset = tTrainingBatchSize;
            //}
            for (int d = 0; d < tTrainingBatchSize; d++)
                UpdateAllDeviationGradients(pTrainingData[d]);
            ApplyAllDiviationGradients(pLearnRate / tTrainingBatchSize);
            if (pLog)
            {
                LogManager.LogANNGradients(layer);
            }
            ClearAllDeviationGradients();
        }

        public void UpdateAllDeviationGradients(TrainingData pData)
        {
            double[] tNEVCAs;
            CalculateOutputs(pData.inputs);
            NeuralNetworkLayer tNNL = outputLayer;
            tNNL.UpdateLayerGradients(tNEVCAs = tNNL.CalculateOutputLayerNEVCAs(pData.expectedOutputs));
            for (int l = layerAmount - 2; l > -1; l--)
            {
                NeuralNetworkLayer tHiddenNNL = layer[l];
                tHiddenNNL.UpdateLayerGradients(tNEVCAs = tHiddenNNL.CalculateHiddenLayerNEVCAs(tNNL, tNEVCAs));
                tNNL = tHiddenNNL;
            }
        }

        public void ClearAllDeviationGradients()
        {
            for (int l = 0; l < layerAmount; l++)
                layer[l].ClearLayerGradients();
        }

        public void ApplyAllDiviationGradients(double pRate)
        {
            for (int l = 0; l < layerAmount; l++)
                layer[l].ApplyDiviationGradients(pRate);
        }

        public void GenerateRandomNetwork(System.Random rng, double minValWeights, double maxValWeights, double minValBiases, double maxValBiases)
        {
            for (int l = 0; l < layerAmount; l++)
                layer[l].SetAndGenerateRandomLayerValues(rng, minValWeights, maxValWeights, minValBiases, maxValBiases);
        }

        public double[] CalculateOutputs(double[] pInputs)
        {
            for (int l = 0; l < layerAmount; l++)
                pInputs = layer[l].CalculateLayerOutputs(pInputs);
            return pInputs;
        }

        public double CalculateDeviation(TrainingData[] pDataArr)
        {
            double rDeviation = 0d;
            int arrLen = pDataArr.Length;
            for (int iData = 0; iData < arrLen; iData++)
                rDeviation += CalculateDeviation(pDataArr[iData]);
            return rDeviation / arrLen;
        }

        public double CalculateDeviation(TrainingData pData)
        {
            double rDeviation = 0d;
            double[] nnlOutputs = CalculateOutputs(pData.inputs);
            NeuralNetworkLayer outputNNL = outputLayer;
            for (int outputNode = 0; outputNode < outputAmount; outputNode++)
                rDeviation += diviationFunction(nnlOutputs[outputNode] - pData.expectedOutputs[outputNode]);
            return rDeviation;
        }

        public double[] GetSortedOutputs(double[] pInputs)
        {
            pInputs = CalculateOutputs(pInputs);
            Array.Sort(pInputs);
            return pInputs;
        }

        public override string ToString()
        {
            string s = "";
            for (int l = 0; l < layerAmount; l++)
                s += (l + 1) + ". " + layer[l].ToString() + "\n";
            return s;
        }
    }

    public class NeuralNetworkLayer
    {
        public int previousLayerNodeAmount { get; private set; }
        public int thisLayerNodeAmount { get; private set; }

        public double[,] weights { get; private set; }
        public double[] biases { get; private set; }

        public double[,] deviationGradientWeights { get; private set; }
        public double[] deviationGradientBiases { get; private set; }

        private double[] lastReceivedInputValues, lastReceivedNEVs, lastGivenOutputs;

        private Func<double, double> neuronFunction, neuronDerivativeFunction, diviationFunction, deviationDerivativeFunction;
        private Func<double[], int, double[]> additionalNeuronFunction, derivativeOfAdditionalNeuronFunction;

        private bool hasAdditionalNeuronFunction = false;

        public NeuralNetworkLayer(int pPrevLayerNodeAmount, int pThisLayerNodeAmount, Func<double, double> pNeuronFunction, Func<double, double> pNeuronDerivativeFunction, Func<double, double> pDiviationFunction, Func<double, double> pDeviationDerivativeFunction)
        {
            neuronFunction = pNeuronFunction;
            neuronDerivativeFunction = pNeuronDerivativeFunction;
            diviationFunction = pDiviationFunction;
            deviationDerivativeFunction = pDeviationDerivativeFunction;
            previousLayerNodeAmount = pPrevLayerNodeAmount;
            thisLayerNodeAmount = pThisLayerNodeAmount;

            lastReceivedNEVs = new double[thisLayerNodeAmount];
            lastGivenOutputs = new double[thisLayerNodeAmount];
            weights = new double[pPrevLayerNodeAmount, pThisLayerNodeAmount];
            biases = new double[pThisLayerNodeAmount];
            deviationGradientWeights = new double[pPrevLayerNodeAmount, pThisLayerNodeAmount];
            deviationGradientBiases = new double[pThisLayerNodeAmount];
        }

        public void SetNeuronFunctions(Func<double, double> pFunc, Func<double, double> pDerivativeFunc)
        {
            neuronFunction = pFunc;
            neuronDerivativeFunction = pDerivativeFunc;
        }

        public void AddAdditionalNeuronFunction(Func<double[], int, double[]> pFunc, Func<double[], int, double[]> pDerivativeFunc)
        {
            hasAdditionalNeuronFunction = true;
            additionalNeuronFunction = pFunc;
            derivativeOfAdditionalNeuronFunction = pDerivativeFunc;
        }

        public double[] CalculateLayerOutputs(double[] pInputs)
        {
            lastReceivedInputValues = pInputs;
            double[] rOutputs = new double[thisLayerNodeAmount];
            for (int outp = 0; outp < thisLayerNodeAmount; outp++)
            {
                double d = biases[outp];
                for (int inp = 0; inp < previousLayerNodeAmount; inp++)
                    d += pInputs[inp] * weights[inp, outp];
                lastGivenOutputs[outp] = rOutputs[outp] = neuronFunction(lastReceivedNEVs[outp] = d);
            }
            if (hasAdditionalNeuronFunction) return additionalNeuronFunction(rOutputs, thisLayerNodeAmount);
            return rOutputs;
        }

        public double[] CalculateOutputLayerNEVCAs(double[] pExpectedOutputs)
        {
            double[] rNEVCA = new double[thisLayerNodeAmount];
            for (int outputNode = 0; outputNode < thisLayerNodeAmount; outputNode++)
                rNEVCA[outputNode] = neuronDerivativeFunction(lastReceivedNEVs[outputNode]) * deviationDerivativeFunction(lastGivenOutputs[outputNode] - pExpectedOutputs[outputNode]);

            if (hasAdditionalNeuronFunction)
            {
                lastGivenOutputs = derivativeOfAdditionalNeuronFunction(lastGivenOutputs, thisLayerNodeAmount);
                for (int outputNode = 0; outputNode < thisLayerNodeAmount; outputNode++)
                    rNEVCA[outputNode] *= lastGivenOutputs[outputNode];
            }

            return rNEVCA;
        }

        public double[] CalculateHiddenLayerNEVCAs(NeuralNetworkLayer pPreviousBackpropagationLayer, double[] pPreviousBackpropagationNEVCAs)
        {
            double[] curBackpropagationNEVCAs = new double[thisLayerNodeAmount];
            int tPreviousBackpropagationLayerLen = pPreviousBackpropagationLayer.thisLayerNodeAmount;
            for (int outputNode = 0; outputNode < thisLayerNodeAmount; outputNode++)
            {
                double tNEVCA = 0d;
                for (int nextLayerNode = 0; nextLayerNode < tPreviousBackpropagationLayerLen; nextLayerNode++)
                    tNEVCA += pPreviousBackpropagationLayer.weights[outputNode, nextLayerNode] * pPreviousBackpropagationNEVCAs[nextLayerNode];
                curBackpropagationNEVCAs[outputNode] = tNEVCA * neuronDerivativeFunction(lastReceivedNEVs[outputNode]);
            }
            if (hasAdditionalNeuronFunction)
            {
                lastGivenOutputs = derivativeOfAdditionalNeuronFunction(lastGivenOutputs, thisLayerNodeAmount);
                for (int outputNode = 0; outputNode < thisLayerNodeAmount; outputNode++)
                    curBackpropagationNEVCAs[outputNode] *= lastGivenOutputs[outputNode];
            }
            return curBackpropagationNEVCAs;
        }

        public void UpdateLayerGradients(double[] pNEVCA)
        {
            for (int outputNode = 0; outputNode < thisLayerNodeAmount; outputNode++)
            {
                double tNEVCA = pNEVCA[outputNode];
                for (int inputNode = 0; inputNode < previousLayerNodeAmount; inputNode++)
                    deviationGradientWeights[inputNode, outputNode] += lastReceivedInputValues[inputNode] * tNEVCA;
                deviationGradientBiases[outputNode] += tNEVCA;
            }
        }

        public void ClearLayerGradients()
        {
            for (int outputNode = 0; outputNode < thisLayerNodeAmount; outputNode++)
            {
                for (int inputNode = 0; inputNode < previousLayerNodeAmount; inputNode++)
                    deviationGradientWeights[inputNode, outputNode] = 0;
                deviationGradientBiases[outputNode] = 0;
            }
        }

        public void ApplyDiviationGradients(double pRate)
        {
            for (int outputNode = 0; outputNode < thisLayerNodeAmount; outputNode++)
            {
                biases[outputNode] -= deviationGradientBiases[outputNode] * pRate;
                for (int inputNode = 0; inputNode < previousLayerNodeAmount; inputNode++)
                    weights[inputNode, outputNode] -= Math.Min(Math.Max(deviationGradientWeights[inputNode, outputNode], -1), 1) * pRate;
            }
        }

        public void SetLayerValues(double[,] pWeights, double[] pBiases)
        {
            weights = pWeights;
            biases = pBiases;
        }

        public void SetAndGenerateRandomLayerValues(System.Random rng, double minValWeights, double maxValWeights, double minValBiases, double maxValBiases)
        {
            double valDifWei = maxValWeights - minValWeights, valDifBia = maxValBiases - minValBiases;

            for (int n = 0; n < thisLayerNodeAmount; n++)
            {
                biases[n] = rng.NextDouble() * valDifBia + minValBiases;
                for (int p = 0; p < previousLayerNodeAmount; p++)
                    weights[p, n] = rng.NextDouble() * valDifWei + minValWeights;
            }
        }

        public override string ToString()
        {
            return UtilityFunctions.ObjectValueStringRepresentation("NeuralNetworkLayer", "$PreviosNodeCount", previousLayerNodeAmount, "$LayerNodeCount", thisLayerNodeAmount);
        }
    }

    #endregion

    #region    | GENERAL |

    [System.Serializable]
    public class TrainingData
    {
        public double[] inputs, expectedOutputs;

        public TrainingData(double[] pInputs, double[] pExpectedOutputs)
        {
            inputs = pInputs;
            expectedOutputs = pExpectedOutputs;
        }

        public static TrainingData[][] CreateMiniBatches(TrainingData[] pTrainingData, int pBatchSize)
        {
            int l = pTrainingData.Length, b = 1 + (l - l % pBatchSize) / pBatchSize, t = 0;
            if (l % pBatchSize == 0) b--;
            TrainingData[][] rBatches = new TrainingData[b][];
            for (int i = 0; i < b; i++)
            {
                t += pBatchSize;
                if (t > l) t = l;
                List<TrainingData> ttd = new List<TrainingData>();
                for (int j = i * pBatchSize; j < t; j++)
                    ttd.Add(pTrainingData[j]);

                rBatches[i] = ttd.ToArray();
            }
            return rBatches;
        }
    }

    public static class NeuronFunctions
    {
        #region    | DEVIATION FUNCTIONS |

        public static double LinearDeviation(double val)
        {
            return val;
        }

        public static double SquaredDeviation(double val)
        {
            return val * val;
        }

        public static double SquareDeviationDerivative(double val)
        {
            return 2 * val;
        }

        public static double ThirdPowerDeviation(double val)
        {
            return Math.Abs(val * val * val);
        }

        public static double ThilonicSpikeDeviation(double val)
        {
            if (val < 0.33d) return Math.Abs(val);
            return 3 * val * val;
        }

        #endregion

        #region    | ACTIVATION FUNCTIONS |

        #region ** Linear **

        public static double Linear(double val)
        {
            return val;
        }

        public static double LinearDerivative(double val)
        {
            return 1;
        }

        #endregion

        #region ** Sigmoid **

        public static double Sigmoid(double val)
        {
            return 1d / (1d + Math.Exp(-val));
        }

        public static double SigmoidDerivative(double val)
        {
            return (val = Sigmoid(val)) * (1 - val);
        }

        #endregion

        #region ** SiLU ** 

        public static double SiLU(double val)
        {
            return val / (1 + Math.Exp(-val));
        }

        public static double SiLUDerivative(double val)
        {
            double d = Math.Exp(-val);
            return (d + val * d + 1) / ((d += 1) * d);
        }

        #endregion

        #region ** ReLU ** 

        public static double ReLU(double val)
        {
            if (val < 0d) return 0d;
            return val;
        }

        public static double ReLUDerivative(double val)
        {
            return (val > 0d) ? 1d : 0d;
        }

        #endregion

        #region ** Hyperbolic Tangents ** 

        public static double HyperbolicTangent1(double val)
        {
            return 2d / (1d + Math.Exp(-val)) - 1d;
        }

        public static double HyperbolicTangent1Derivative(double val)
        {
            return 2 * (val = Math.Exp(-val)) / ((val + 1) * (val + 1));
        }

        public static double HyperbolicTangent2(double val)
        {
            return ((val = Math.Exp(2 * val)) - 1) / (val + 1);
        }

        public static double HyperbolicTangent2Derivative(double val)
        {
            return 1 - (val = HyperbolicTangent2(val)) * val;
        }

        #endregion

        #region ** Step ** 

        public static double Step(double val)
        {
            return (val > 0d) ? 1d : 0d;
        }

        public static double StepDerivative(double val)
        {
            return (val == 0d) ? 1d : 0d;
        }

        #endregion

        #region ** Softmax **

        public static double[] Softmax(double[] vals, int len)
        {
            double sum = 0d;
            for (int i = 0; i < len; i++) sum += (vals[i] = Math.Exp(vals[i]));
            for (int i = 0; i < len; i++) vals[i] /= sum;
            return vals;
        }

        public static double[] SoftmaxDerivative(double[] vals, int len)
        {
            double sum = 0d;
            for (int i = 0; i < len; i++) sum += (vals[i] = Math.Exp(vals[i]));
            double squaredSum = sum * sum;
            for (int i = 0; i < len; i++) vals[i] = (vals[i] * sum - vals[i] * vals[i]) / squaredSum;
            return vals;
        }

        #endregion

        #endregion
    }

    public static class LogManager
    {
        private static string logFilePath = "";

        public static void SetLogFile(string tLF)
        {
            logFilePath = tLF;
            if (!File.Exists(logFilePath)) File.CreateText(logFilePath);
            else File.WriteAllText(logFilePath, "");
        }

        public static void LogText(string pStr)
        {
            File.AppendAllText(logFilePath, pStr);
        }

        public static void LogANNGradients(NeuralNetworkLayer[] pLayers)
        {
            int layerAmount = pLayers.Length;

            for (int l = 0; l < layerAmount; l++)
            {
                NeuralNetworkLayer tnnl = pLayers[l];
                int tLayerSize = tnnl.thisLayerNodeAmount, tPrevLayerSize = tnnl.previousLayerNodeAmount;
                string layerString = "\n\n  - - - { LAYER " + l + " } - - -";
                for (int i = 0; i < tLayerSize; i++)
                {
                    layerString += "\n        (" + i + ") Bias-Gradient: " + tnnl.deviationGradientBiases[i] + " | Weight-Gradients: ";
                    for (int j = 0; j < tPrevLayerSize; j++)
                    {
                        if (j != 0) layerString += ", ";
                        layerString += tnnl.deviationGradientWeights[j, i];
                    }
                }
                File.AppendAllText(logFilePath, layerString);
            }
        }
    }

    #endregion

    #endregion

    #region | UTILITY |

    public static class UtilityFunctions
    {
        public static string ObjectValueStringRepresentation(string objectName, params object[] values)
        {
            string tFirstParam = values[0].ToString();
            string rStr = objectName + " { \n    [" + tFirstParam.Substring(1, tFirstParam.Length - 1) + " = ";
            int vLength = values.Length;
            for (int v = 1; v < vLength; v++)
            {
                object o = values[v];
                string ostr = o.ToString();
                if (o.GetType() == typeof(string) && ostr[0] == '$')
                    rStr = rStr.Substring(0, rStr.Length - 2) + "]\n    [" + ostr.Substring(1, ostr.Length - 1) + " = ";
                else rStr += o + ", ";
            }
            return rStr.Substring(0, rStr.Length - 2) + "]\n}";
        }
    }

    #endregion
}
