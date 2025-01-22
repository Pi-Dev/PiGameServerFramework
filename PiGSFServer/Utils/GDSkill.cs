using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiGSF.Ratings
{
    using System;

    /// <summary>
    /// PeterSvP's Gaussian Distribution Skill system
    /// Inspired by TrueSkill, but passed via ChatGPT so the code base is inaccurate + not infringe patents.
    /// Modified by PeterSvP to support contribution based ranking.
    /// Use at your own risk! NEVER tested in an ESport!
    /// http://research.microsoft.com/apps/pubs/default.aspx?id=67956
    /// </summary>
    public static class GDSkill
    {
        public struct Params
        {
            public double Mu;
            public double Sigma;

            public Params(double mu, double sigma)
            {
                Mu = mu;
                Sigma = sigma;
            }
            public static Params Default => new Params(25, 25.0/3.0);

            // Calculate MMR: mult * (μ - k * σ)
            public double MMR(double scalingFactor = 3, double mult = 1)
            {
                return mult * (Mu - scalingFactor * Sigma);
            }

            public override string ToString()
            {
                return $"μ={Mu:F2}, σ={Sigma:F2}, MMR={MMR():F2}";
            }
        }

        public enum GameOutcome
        {
            Player1Won,
            Player2Won,
            Draw
        }

        public static List<Params> UpdateRatingsByContributions(
            List<(Params Params, float Contribution)> playerData,
            double beta = 4.1667)
        {
            if (playerData == null || playerData.Count == 0)
                throw new ArgumentException("Player data cannot be null or empty.", nameof(playerData));

            // Normalize contributions to sum to 1
            double totalContribution = playerData.Sum(p => p.Contribution);

            if (totalContribution == 0)
            {
                // No contributions: Return the original ratings unchanged
                return playerData.Select(p => p.Params).ToList();
            }

            var normalizedData = playerData.Select(p => (p.Params, Contribution: p.Contribution / totalContribution)).ToList();

            // Compute average skill and uncertainty for the group
            double averageMu = normalizedData.Average(p => p.Params.Mu);
            double averageSigma = Math.Sqrt(normalizedData.Average(p => Math.Pow(p.Params.Sigma, 2)));

            // Adjust each player's parameters based on their contribution
            var updatedParams = new List<Params>();
            foreach (var (playerParams, contribution) in normalizedData)
            {
                // Update μ based on contribution (scale adjustment around group mean)
                double deltaMu = contribution * (playerParams.Mu - averageMu);

                // Update σ to reflect reduced uncertainty
                double deltaSigma = averageSigma * (1 - Math.Abs(contribution)); // Smaller contribution -> more uncertainty

                double newMu = playerParams.Mu + deltaMu;
                double newSigma = Math.Max(playerParams.Sigma - deltaSigma, 1e-3); // Ensure σ does not become negative

                updatedParams.Add(new Params(newMu, newSigma));
            }

            return updatedParams;
        }



        public static Params GetTeamParams(IList<Params> team)
        {
            if (team == null || team.Count == 0)
                throw new ArgumentException("Team must contain at least one player.", nameof(team));

            double aggregateMu = team.Sum(p => p.Mu);
            double aggregateSigma = Math.Sqrt(team.Sum(p => Math.Pow(p.Sigma, 2)));
            return new Params(aggregateMu, aggregateSigma);
        }

        public static (Params player1, Params player2) UpdateRatings(
            Params player1Params,
            Params player2Params,
            GameOutcome outcome,
            double beta = 4.1667)
        {
            // Extract parameters
            double mu1 = player1Params.Mu;
            double sigma1 = player1Params.Sigma;
            double mu2 = player2Params.Mu;
            double sigma2 = player2Params.Sigma;

            // Compute performance difference mean and variance
            double muDelta = mu1 - mu2;
            double sigmaDelta = Math.Sqrt(Math.Pow(sigma1, 2) + Math.Pow(sigma2, 2) + 2 * Math.Pow(beta, 2));

            // Determine the value of t based on outcome
            double t = outcome switch
            {
                GameOutcome.Player1Won => muDelta / sigmaDelta,
                GameOutcome.Player2Won => -muDelta / sigmaDelta,
                GameOutcome.Draw => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), "Invalid game outcome.")
            };

            double pdfT = GaussianPDF(t);
            double cdfT = GaussianCDF(t);

            // Handle numerical instability: if cdfT is too small, default to minor adjustments
            if (cdfT < 1e-9)
            {
                cdfT = 1e-9; // Minimum threshold to avoid instability
                pdfT = GaussianPDF(0); // Neutral adjustment
            }

            // V and W functions
            double v = pdfT / cdfT;
            double w = v * (v + t);

            // Update means
            double mu1New = mu1 + (Math.Pow(sigma1, 2) / sigmaDelta) * v * (outcome == GameOutcome.Player2Won ? -1 : 1);
            double mu2New = mu2 - (Math.Pow(sigma2, 2) / sigmaDelta) * v * (outcome == GameOutcome.Player2Won ? -1 : 1);

            // Update variances
            double sigma1New = Math.Sqrt(Math.Pow(sigma1, 2) * (1 - (Math.Pow(sigma1, 2) / Math.Pow(sigmaDelta, 2)) * w));
            double sigma2New = Math.Sqrt(Math.Pow(sigma2, 2) * (1 - (Math.Pow(sigma2, 2) / Math.Pow(sigmaDelta, 2)) * w));

            // Return updated ratings
            return (
                new Params(mu1New, sigma1New),
                new Params(mu2New, sigma2New)
            );
        }

        // Gaussian Probability Density Function (PDF)
        private static double GaussianPDF(double x)
        {
            return Math.Exp(-0.5 * Math.Pow(x, 2)) / Math.Sqrt(2 * Math.PI);
        }

        // Gaussian Cumulative Distribution Function (CDF)
        private static double GaussianCDF(double x)
        {
            return 0.5 * (1.0 + Erf(x / Math.Sqrt(2)));
        }

        // Error function approximation
        private static double Erf(double x)
        {
            // Abramowitz & Stegun approximation
            double a1 = 0.254829592;
            double a2 = -0.284496736;
            double a3 = 1.421413741;
            double a4 = -1.453152027;
            double a5 = 1.061405429;
            double p = 0.3275911;

            double sign = x < 0 ? -1 : 1;
            x = Math.Abs(x);

            double t = 1.0 / (1.0 + p * x);
            double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            return sign * y;
        }
    }


}
