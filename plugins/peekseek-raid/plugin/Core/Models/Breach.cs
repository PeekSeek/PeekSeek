using System.Collections.Generic;
using System.Linq;

namespace PeekSeekRaid.Core.Models;

/// One piece a ray from the flag crosses on its way out of the base, nearest first.
public class BreachPiece {
    /// Demolition charges to break it.
    public float Charges;

    /// Charges to break the same piece in maple, or null when it has no maple form
    /// (a door, or a piece already wood).
    public float? AsMaple;

    public BreachPiece(float charges, float? asMaple = null) {
        Charges = charges;
        AsMaple = asMaple;
    }
}

/// How many charges it takes to reach the flag from outside, and how to bring that
/// down when a base has been built too well.
///
/// A ray is walked from the flag outward and every piece it crosses is summed; the
/// breach cost is the cheapest ray. That is the number of charges a team that knows
/// the base needs, which is exactly the number a guarantee about getting in has to
/// bound.
public static class Breach {
    public static float Cost(IEnumerable<BreachPiece> ray) => ray.Sum(piece => piece.Charges);

    /// The cheapest ray, by index, and its cost. -1 and 0 for no rays at all.
    public static (int Ray, float Cost) Cheapest(IList<List<BreachPiece>> rays) {
        int best = -1;
        float cost = float.MaxValue;
        for (int i = 0; i < rays.Count; i++) {
            float here = Cost(rays[i]);
            if (here < cost) {
                best = i;
                cost = here;
            }
        }

        return best < 0 ? (-1, 0f) : (best, cost);
    }

    /// Which pieces of a ray to turn into maple, outermost first, until the ray costs
    /// `limit` or less. The outside of a base is what a raider meets first, and a flag
    /// that keeps its inner walls is still a defence. Indices into the ray, nearest the
    /// flag first; the ray may still be over when it runs out of metal.
    public static List<int> Downgrades(IList<BreachPiece> ray, float limit) {
        float cost = Cost(ray);
        List<int> changed = [];

        for (int i = ray.Count - 1; i >= 0 && cost > limit; i--) {
            if (ray[i].AsMaple is not float maple || maple >= ray[i].Charges) continue;

            cost -= ray[i].Charges - maple;
            changed.Add(i);
        }

        changed.Reverse();
        return changed;
    }
}
