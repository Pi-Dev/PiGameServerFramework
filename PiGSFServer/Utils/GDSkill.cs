using System;
using System.Collections.Generic;
using System.Linq;

namespace PiGSF.Ratings
{
    /// <summary>
    /// PeterSvP's Gaussian Distribution Skill system
    /// Inspired by TrueSkill, but passed via ChatGPT so the code base is inaccurate + not infringe patents.
    /// Modified by PeterSvP to support contribution-based ranking.
    /// Use at your own risk! NEVER tested in an ESport!
    /// http://research.microsoft.com/apps/pubs/default.aspx?id=67956
    /// </summary>
    public static class GDSkill
    {
        public class GDRating
        {
            public double Mu { get; set; }
            public double Sigma { get; set; }
            public double MMRMult = 10;
            public double MMRScale = 3;

            public GDRating(double mu, double sigma)
            {
                Mu = mu;
                Sigma = sigma;
            }

            // Calculate MMR: mult * (μ - k * σ)
            public double MMR() => MMRMult * (Mu - MMRScale * Sigma);
            public double MMR(double mult, double scalingFactor = 3) => mult * (Mu - scalingFactor * Sigma);

            public override string ToString()
            {
                return $"μ={Mu:F2}, σ={Sigma:F2}, MMR={MMR():F2}";
            }
            public static GDRating Default(float MMRMult = 1, float MMRScale = 3)
            {
                return new GDRating(25, 25.0 / 3.0) { MMRMult = MMRMult, MMRScale = MMRScale };
            }
        }

        public static GDRating GetTeamParams(IList<GDRating> team)
        {
            double aggregateMu = team.Sum(p => p.Mu);
            double aggregateSigma = Math.Sqrt(team.Sum(p => Math.Pow(p.Sigma, 2)));
            return new GDRating(aggregateMu, aggregateSigma);
        }

        public static void ApplyTeamRatings(GDRating teamParams, IList<GDRating> team)
        {
            var originalTeamParams = GetTeamParams(team);
            double muDelta = teamParams.Mu - originalTeamParams.Mu;
            double sigmaDelta = teamParams.Sigma - originalTeamParams.Sigma;

            foreach (var player in team)
            {
                player.Mu += muDelta / team.Count;
                player.Sigma = Math.Max(player.Sigma + sigmaDelta / team.Count, 1e-3);
            }
        }

        public enum GameOutcome
        {
            Side1Won,
            Side2Won,
            Draw
        }

        public static void UpdateRatingsByContributions(
            List<(GDRating Rating, float Contribution)> playerData,
            double beta = 4.1667)
        {
            // Normalize contributions to sum to 1
            double totalContribution = playerData.Sum(p => p.Contribution);
            if (totalContribution == 0)
            {
                // No contributions: Return unchanged
                return;
            }

            var normalizedData = playerData.Select(p => (p.Rating, Contribution: p.Contribution / totalContribution)).ToList();

            // Compute average skill and uncertainty for the group
            double averageMu = normalizedData.Average(p => p.Rating.Mu);
            double averageSigma = Math.Sqrt(normalizedData.Average(p => Math.Pow(p.Rating.Sigma, 2)));

            // Adjust each player's parameters based on their contribution
            foreach (var (rating, contribution) in normalizedData)
            {
                double deltaMu = contribution * (rating.Mu - averageMu);
                double deltaSigma = averageSigma * (1 - Math.Abs(contribution));
                rating.Mu += deltaMu;
                rating.Sigma = Math.Max(rating.Sigma - deltaSigma, 1e-3);
            }
        }

        public static void UpdateRatings(
            GDRating side1, GDRating side2, GameOutcome outcome, double beta = 4.1667)
        {
            double mu1 = side1.Mu;
            double sigma1 = side1.Sigma;
            double mu2 = side2.Mu;
            double sigma2 = side2.Sigma;

            double muDelta = mu1 - mu2;
            double sigmaDelta = Math.Sqrt(Math.Pow(sigma1, 2) + Math.Pow(sigma2, 2) + 2 * Math.Pow(beta, 2));

            double t = outcome switch
            {
                GameOutcome.Side1Won => muDelta / sigmaDelta,
                GameOutcome.Side2Won => -muDelta / sigmaDelta,
                GameOutcome.Draw => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), "Invalid game outcome.")
            };

            double pdfT = GaussianPDF(t);
            double cdfT = GaussianCDF(t);

            if (cdfT < 1e-9)
            {
                cdfT = 1e-9;
                pdfT = GaussianPDF(0);
            }

            double v = pdfT / cdfT;
            double w = v * (v + t);

            side1.Mu += (Math.Pow(sigma1, 2) / sigmaDelta) * v * (outcome == GameOutcome.Side2Won ? -1 : 1);
            side2.Mu -= (Math.Pow(sigma2, 2) / sigmaDelta) * v * (outcome == GameOutcome.Side2Won ? -1 : 1);

            side1.Sigma = Math.Sqrt(Math.Pow(sigma1, 2) * (1 - (Math.Pow(sigma1, 2) / Math.Pow(sigmaDelta, 2)) * w));
            side2.Sigma = Math.Sqrt(Math.Pow(sigma2, 2) * (1 - (Math.Pow(sigma2, 2) / Math.Pow(sigmaDelta, 2)) * w));
        }

        private static double GaussianPDF(double x)
        {
            return Math.Exp(-0.5 * Math.Pow(x, 2)) / Math.Sqrt(2 * Math.PI);
        }

        private static double GaussianCDF(double x)
        {
            return 0.5 * (1.0 + Erf(x / Math.Sqrt(2)));
        }

        private static double Erf(double x)
        {
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
