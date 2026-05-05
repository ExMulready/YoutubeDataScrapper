using System.Text.Json;
using System.Text.Json.Serialization;

namespace YoutubeResearchMcp.ML;

public record TrainingProgress(int Epoch, int TotalEpochs, double Loss);

public class NeuralNetwork
{
    public static readonly int[] LayerSizes = [12, 24, 12, 1];
    private const int Layers = 3;

    private const double Lr      = 0.001;
    private const double Beta1   = 0.9;
    private const double Beta2   = 0.999;
    private const double Epsilon = 1e-8;

    private double[][][] _w;
    private double[][]   _b;

    private double[][][] _mW, _vW;
    private double[][]   _mB, _vB;
    private int          _t;

    public double[] NormMean   { get; private set; } = new double[LayerSizes[0]];
    public double[] NormStdDev { get; private set; } = new double[LayerSizes[0]];

    /// <summary>Initialises a new untrained network with He-initialised weights.</summary>
    public NeuralNetwork()
    {
        (_w, _b)   = Allocate();
        (_mW, _mB) = Allocate();
        (_vW, _vB) = Allocate();
        InitialiseWeights(new Random(42));
    }

    /// <summary>Restores a network from a serialised snapshot, cloning all arrays to ensure independence.</summary>
    public NeuralNetwork(NetworkSnapshot snap)
    {
        _w = snap.Weights.Select(l => l.Select(r => (double[])r.Clone()).ToArray()).ToArray();
        _b = snap.Biases.Select(l => (double[])l.Clone()).ToArray();
        (_mW, _mB) = Allocate();
        (_vW, _vB) = Allocate();
        _t = snap.Timestep;
        NormMean   = (double[])snap.NormMean.Clone();
        NormStdDev = (double[])snap.NormStdDev.Clone();
    }

    /// <summary>Returns a deep-cloned serialisable snapshot of the current weights and normalisation statistics.</summary>
    public NetworkSnapshot ToSnapshot() => new()
    {
        Weights    = _w.Select(l => l.Select(r => (double[])r.Clone()).ToArray()).ToArray(),
        Biases     = _b.Select(l => (double[])l.Clone()).ToArray(),
        Timestep   = _t,
        NormMean   = (double[])NormMean.Clone(),
        NormStdDev = (double[])NormStdDev.Clone()
    };

    /// <summary>Deserialises a network snapshot from JSON and returns a restored NeuralNetwork.</summary>
    public static NeuralNetwork FromJson(string json)
    {
        var snap = JsonSerializer.Deserialize<NetworkSnapshot>(json)
            ?? throw new InvalidOperationException("Cannot deserialise network snapshot.");
        return new NeuralNetwork(snap);
    }

    /// <summary>Serialises the current network state to a compact JSON string.</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(ToSnapshot(), new JsonSerializerOptions { WriteIndented = false });

    /// <summary>
    /// Trains the network using mini-batch Adam gradient descent with early stopping.
    /// Reports progress every 10 epochs via <paramref name="progress"/> if provided.
    /// Returns the final mean-squared error over all training samples.
    /// </summary>
    public double Train(IReadOnlyList<double[]> rawFeatures, IReadOnlyList<double> labels,
        int epochs = 100, int batchSize = 32,
        IProgress<TrainingProgress>? progress = null)
    {
        ComputeNormalisation(rawFeatures);

        var xs = rawFeatures.Select(Normalise).ToArray();
        double lastLoss = 0;
        var lossWindow  = new Queue<double>(11);

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            lastLoss = 0;
            var indices = Enumerable.Range(0, xs.Length).OrderBy(_ => Random.Shared.Next()).ToArray();

            for (int start = 0; start < indices.Length; start += batchSize)
            {
                var batch = indices.Skip(start).Take(batchSize).ToArray();
                var (dW, dB) = ComputeGradients(xs, labels, batch);
                ApplyAdam(dW, dB);
            }

            foreach (var i in Enumerable.Range(0, xs.Length))
            {
                var pred = Forward(xs[i]);
                var diff = pred - labels[i];
                lastLoss += diff * diff;
            }
            lastLoss /= xs.Length;

            if ((epoch + 1) % 10 == 0 || epoch == epochs - 1)
                progress?.Report(new TrainingProgress(epoch + 1, epochs, lastLoss));

            // Early stopping: if loss improvement over the last 10 epochs is negligible, stop.
            lossWindow.Enqueue(lastLoss);
            if (lossWindow.Count > 10) lossWindow.Dequeue();

            if (lossWindow.Count == 10)
            {
                var arr      = lossWindow.ToArray();
                var earlyAvg = arr.Take(5).Average();
                var lateAvg  = arr.Skip(5).Average();
                if (earlyAvg - lateAvg < 1e-5) break;
            }
        }

        return lastLoss;
    }

    /// <summary>
    /// Returns a performance score in [0, 1] for raw (unnormalised) features.
    /// Throws if the feature vector has the wrong length or contains NaN/Infinity.
    /// </summary>
    public double Predict(double[] rawFeatures)
    {
        if (rawFeatures.Length != LayerSizes[0])
            throw new ArgumentException(
                $"Expected {LayerSizes[0]} features, got {rawFeatures.Length}.", nameof(rawFeatures));

        if (rawFeatures.Any(f => double.IsNaN(f) || double.IsInfinity(f)))
            throw new ArgumentException(
                "Feature vector contains NaN or Infinity.", nameof(rawFeatures));

        var x = Normalise(rawFeatures);
        return Forward(x);
    }

    /// <summary>Runs a forward pass on normalised inputs and returns the scalar output.</summary>
    private double Forward(double[] x)
    {
        var a = x;
        for (int l = 0; l < Layers; l++)
        {
            var z = new double[_w[l].Length];
            for (int j = 0; j < _w[l].Length; j++)
            {
                z[j] = _b[l][j];
                for (int i = 0; i < a.Length; i++)
                    z[j] += _w[l][j][i] * a[i];
            }
            a = l < Layers - 1 ? ReLU(z) : Sigmoid(z);
        }
        return a[0];
    }

    /// <summary>Forward pass that also caches activations and pre-activations needed for backpropagation.</summary>
    private (double[][] activations, double[][] preActivations) ForwardCached(double[] x)
    {
        var activations    = new double[Layers + 1][];
        var preActivations = new double[Layers][];

        activations[0] = x;

        for (int l = 0; l < Layers; l++)
        {
            var z = new double[_w[l].Length];
            for (int j = 0; j < _w[l].Length; j++)
            {
                z[j] = _b[l][j];
                for (int i = 0; i < activations[l].Length; i++)
                    z[j] += _w[l][j][i] * activations[l][i];
            }
            preActivations[l] = z;
            activations[l + 1] = l < Layers - 1 ? ReLU(z) : Sigmoid(z);
        }

        return (activations, preActivations);
    }

    /// <summary>Computes averaged weight and bias gradients over the given mini-batch indices.</summary>
    private (double[][][] dW, double[][] dB) ComputeGradients(
        double[][] xs, IReadOnlyList<double> labels, int[] batchIndices)
    {
        var (dW, dB) = Allocate();

        foreach (var idx in batchIndices)
        {
            var (acts, preActs) = ForwardCached(xs[idx]);

            var delta = new double[Layers][];

            var output = acts[Layers][0];
            delta[Layers - 1] = new double[1];
            delta[Layers - 1][0] = 2.0 * (output - labels[idx]) * SigmoidDerivative(preActs[Layers - 1][0]);

            for (int l = Layers - 2; l >= 0; l--)
            {
                delta[l] = new double[LayerSizes[l + 1]];
                for (int i = 0; i < LayerSizes[l + 1]; i++)
                {
                    double sum = 0;
                    for (int j = 0; j < delta[l + 1].Length; j++)
                        sum += _w[l + 1][j][i] * delta[l + 1][j];
                    delta[l][i] = sum * (preActs[l][i] > 0 ? 1.0 : 0.0);
                }
            }

            for (int l = 0; l < Layers; l++)
            {
                for (int j = 0; j < _w[l].Length; j++)
                {
                    dB[l][j] += delta[l][j];
                    for (int i = 0; i < acts[l].Length; i++)
                        dW[l][j][i] += delta[l][j] * acts[l][i];
                }
            }
        }

        double n = batchIndices.Length;
        for (int l = 0; l < Layers; l++)
        {
            for (int j = 0; j < dB[l].Length; j++)
            {
                dB[l][j] /= n;
                for (int i = 0; i < dW[l][j].Length; i++)
                    dW[l][j][i] /= n;
            }
        }

        return (dW, dB);
    }

    /// <summary>Updates weights and biases using the Adam optimiser with bias-corrected moment estimates.</summary>
    private void ApplyAdam(double[][][] dW, double[][] dB)
    {
        _t++;
        double bc1 = 1.0 - Math.Pow(Beta1, _t);
        double bc2 = 1.0 - Math.Pow(Beta2, _t);

        for (int l = 0; l < Layers; l++)
        {
            for (int j = 0; j < _w[l].Length; j++)
            {
                _mB[l][j] = Beta1 * _mB[l][j] + (1 - Beta1) * dB[l][j];
                _vB[l][j] = Beta2 * _vB[l][j] + (1 - Beta2) * dB[l][j] * dB[l][j];
                _b[l][j] -= Lr * (_mB[l][j] / bc1) / (Math.Sqrt(_vB[l][j] / bc2) + Epsilon);

                for (int i = 0; i < _w[l][j].Length; i++)
                {
                    _mW[l][j][i] = Beta1 * _mW[l][j][i] + (1 - Beta1) * dW[l][j][i];
                    _vW[l][j][i] = Beta2 * _vW[l][j][i] + (1 - Beta2) * dW[l][j][i] * dW[l][j][i];
                    _w[l][j][i] -= Lr * (_mW[l][j][i] / bc1) / (Math.Sqrt(_vW[l][j][i] / bc2) + Epsilon);
                }
            }
        }
    }

    /// <summary>Computes and stores per-feature mean and standard deviation for z-score normalisation.</summary>
    private void ComputeNormalisation(IReadOnlyList<double[]> xs)
    {
        int f = LayerSizes[0];
        NormMean   = new double[f];
        NormStdDev = new double[f];

        foreach (var x in xs)
            for (int i = 0; i < f; i++)
                NormMean[i] += x[i];

        for (int i = 0; i < f; i++)
            NormMean[i] /= xs.Count;

        foreach (var x in xs)
            for (int i = 0; i < f; i++)
            {
                var d = x[i] - NormMean[i];
                NormStdDev[i] += d * d;
            }

        // Use Bessel's correction (÷ N−1) for an unbiased sample standard deviation.
        for (int i = 0; i < f; i++)
            NormStdDev[i] = Math.Sqrt(NormStdDev[i] / Math.Max(xs.Count - 1, 1) + 1e-8);
    }

    /// <summary>Z-score normalises a feature vector using the stored training statistics.</summary>
    private double[] Normalise(double[] x)
    {
        var n = new double[x.Length];
        for (int i = 0; i < x.Length; i++)
            n[i] = (x[i] - NormMean[i]) / (NormStdDev[i] + 1e-8);
        return n;
    }

    /// <summary>Allocates zero-initialised weight and bias arrays matching the network architecture.</summary>
    private static (double[][][] w, double[][] b) Allocate()
    {
        var w = new double[Layers][][];
        var b = new double[Layers][];
        for (int l = 0; l < Layers; l++)
        {
            int rows = LayerSizes[l + 1];
            int cols = LayerSizes[l];
            w[l] = new double[rows][];
            b[l] = new double[rows];
            for (int j = 0; j < rows; j++)
                w[l][j] = new double[cols];
        }
        return (w, b);
    }

    /// <summary>He-initialises weights scaled by sqrt(2 / fanIn); biases start at zero.</summary>
    private static void InitialiseWeights(Random rng, double[][][] w, double[][] b)
    {
        for (int l = 0; l < Layers; l++)
        {
            double scale = Math.Sqrt(2.0 / LayerSizes[l]);
            for (int j = 0; j < w[l].Length; j++)
            {
                b[l][j] = 0.0;
                for (int i = 0; i < w[l][j].Length; i++)
                    w[l][j][i] = SampleGaussian(rng) * scale;
            }
        }
    }

    /// <summary>Calls the static overload using the network's own weight arrays.</summary>
    private void InitialiseWeights(Random rng) => InitialiseWeights(rng, _w, _b);

    /// <summary>Samples a standard-normal value using the Box-Muller transform.</summary>
    private static double SampleGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
    }

    private static double[] ReLU(double[] z)    => z.Select(v => Math.Max(0, v)).ToArray();
    private static double[] Sigmoid(double[] z) => z.Select(v => 1.0 / (1.0 + Math.Exp(-v))).ToArray();

    private static double SigmoidDerivative(double z)
    {
        var s = 1.0 / (1.0 + Math.Exp(-z));
        return s * (1.0 - s);
    }
}

public class NetworkSnapshot
{
    [JsonPropertyName("weights")]    public required double[][][] Weights    { get; init; }
    [JsonPropertyName("biases")]     public required double[][]   Biases     { get; init; }
    [JsonPropertyName("timestep")]   public int                   Timestep   { get; init; }
    [JsonPropertyName("normMean")]   public required double[]     NormMean   { get; init; }
    [JsonPropertyName("normStdDev")] public required double[]     NormStdDev { get; init; }
}
