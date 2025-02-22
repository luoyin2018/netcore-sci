using System;
using System.Linq;
using static System.Math;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using static System.FormattableString;

using netDxf.Entities;

using static SearchAThing.SciToolkit;
using Newtonsoft.Json;

namespace SearchAThing
{

    /// <summary>
    /// comparer to sort angles so that they follow runs from Start and End given.
    /// for example giving start=5/4PI, end=PI/2 then the set PI/4 and 3/4PI will sorted as 3/4PI and PI/4.
    /// Precondition: at constructor and as input elements angles must already normalized [0,2PI) and
    /// satisfy they in normalized range between given start, end angles
    /// </summary>
    public class NormalizedAngleComparer : IComparer<double>
    {
        public double NormalizedStartAngleRad { get; private set; }
        public double NormalizedEndAngleRad { get; private set; }

        bool StartGtEnd;

        public NormalizedAngleComparer(double normalizedStartAngleRad, double normalizedEndAngleRad)
        {
            NormalizedStartAngleRad = normalizedStartAngleRad;
            NormalizedEndAngleRad = normalizedEndAngleRad;

            StartGtEnd = NormalizedStartAngleRad > NormalizedEndAngleRad;
        }

        public int Compare(double xAngleRad, double yAngleRad)
        {
            if (StartGtEnd)
            {
                var xLtEqEnd = xAngleRad.LessThanOrEqualsTol(TwoPIRadTol, NormalizedEndAngleRad);
                var yLtEqEnd = yAngleRad.LessThanOrEqualsTol(TwoPIRadTol, NormalizedEndAngleRad);

                if (xLtEqEnd && yLtEqEnd) return xAngleRad.CompareToTol(TwoPIRadTol, yAngleRad);

                var xGtEqStart = xAngleRad.GreatThanOrEqualsTol(TwoPIRadTol, NormalizedStartAngleRad);
                var yGtEqStart = yAngleRad.GreatThanOrEqualsTol(TwoPIRadTol, NormalizedStartAngleRad);

                if (xGtEqStart && yGtEqStart) return xAngleRad.CompareToTol(TwoPIRadTol, yAngleRad);

                // because of precondition x, y in start, end normalized range...

                if (xLtEqEnd) // then yGtEqStart
                    return 1;
                else // yLtEqEnd && xGtEqStart
                    return -1;
            }
            else
                return xAngleRad.CompareToTol(TwoPIRadTol, yAngleRad);
        }
    }

    /// <summary>
    /// base geometry for arc 3d entities
    /// </summary>
    public class Arc3D : Geometry, IEdge
    {

        #region IEdge

        public EdgeType EdgeType => EdgeType.Arc3D;

        public bool EdgeContainsPoint(double tol, Vector3D pt) =>
            this.Contains(tol, pt, inArcAngleRange: true, onlyPerimeter: true);

        /// <summary>
        /// returns this arc splitted by break points maintaining order from, to as this Start, End angle.
        /// precondition: breaks must lie on the arc angle range
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="breaks"></param>
        /// <returns></returns>
        public override IEnumerable<Geometry> Split(double tol, IEnumerable<Vector3D> breaks)
        {
            var nAngleComparer = new NormalizedAngleComparer(this.AngleStart, this.AngleEnd);

            var bangles = breaks
                .Select(b => this.PtAngle(tol, b).NormalizeAngle())
                .OrderBy(w => w, nAngleComparer)
                .ToList();

            if (bangles.Count == 0)
            {
                yield return this;
            }

            var addStart = !bangles[0].EqualsTol(TwoPIRadTol, this.AngleStart);
            var addEnd = !bangles[bangles.Count - 1].EqualsTol(tol_rad, this.AngleEnd);

            var res = new List<Geometry>();

            foreach (var bitem in bangles.WithNextPrimitive())
            {
                if (bitem.itemIdx == 0 && addStart && !this.AngleStart.EqualsTol(TwoPIRadTol, bitem.item))
                    res.Add(new Arc3D(CS, Radius, this.AngleStart, bitem.item));

                if (bitem.next.HasValue)
                    res.Add(new Arc3D(CS, Radius, bitem.item, bitem.next.Value));

                else if (bitem.isLast && addEnd && !bitem.item.EqualsTol(TwoPIRadTol, this.AngleEnd))
                    res.Add(new Arc3D(CS, Radius, bitem.item, this.AngleEnd));
            }

            if (!this.Sense)
            {
                res.Reverse();
                res = res.Select(w => w.ToggleSense()).ToList();
            }

            foreach (var x in res.Cast<IEdge>().CheckSense(tol)) yield return (Geometry)x;
        }

        /// <summary>
        /// mid point eval as arc point at angle start + arc angle/2
        /// </summary>
        public override Vector3D MidPoint => PtAtAngle(AngleStart + Angle / 2);

        public bool Equals(double tol, IEdge other, bool includeSense = false)
        {
            if (this.EdgeType != other.EdgeType) return false;

            var oarc = (Arc3D)other;

            if (!Length.EqualsTol(tol, oarc.Length)) return false;

            if (!Radius.EqualsTol(tol, oarc.Radius)) return false;

            if (!Center.EqualsTol(tol, oarc.Center)) return false;

            if (!CS.BaseZ.EqualsTol(NormalizedLengthTolerance, oarc.CS.BaseZ)) return false;

            if (includeSense)
                return
                    this.SGeomFrom.EqualsTol(tol, other.SGeomFrom)
                    &&
                    this.SGeomTo.EqualsTol(tol, other.SGeomTo);

            return
                (this.From.EqualsTol(tol, oarc.From) && this.To.EqualsTol(tol, oarc.To))
                ||
                (this.From.EqualsTol(tol, oarc.To) && this.To.EqualsTol(tol, oarc.From));
        }

        public IEdge EdgeMove(Vector3D delta) => this.Move(delta);

        public string QCadScript(bool final = true) =>
            Invariant($"ARC3\n{SGeomFrom.X},{SGeomFrom.Y}\n{MidPoint.X},{MidPoint.Y}\n{SGeomTo.X},{SGeomTo.Y}\n{(final ? "QQ\n" : "")}");

        public string A0QCadScript => QCadScript();

        #endregion

        #region Geometry

        public override Arc3D Copy() => new Arc3D(this);

        public new Arc3D ToggleSense() => (Arc3D)base.ToggleSense();

        public override IEnumerable<Vector3D> Vertexes
        {
            get
            {
                yield return From;
                yield return To;
            }
        }

        public override Vector3D GeomFrom => From;

        public override Vector3D GeomTo => To;

        double? _Length = null;

        /// <summary>
        /// Length of Arc from start to end
        /// </summary>
        public override double Length
        {
            get
            {
                if (_Length == null) _Length = Angle * Radius;
                return _Length.Value;
            }
        }

        /// <summary>
        /// split arc into pieces and retrieve split points
        /// /// </summary>
        /// <param name="cnt">nr of piece</param>
        /// <param name="include_endpoints">if true returns also boundary points</param>
        public override IEnumerable<Vector3D> Divide(int cnt, bool include_endpoints = false)
        {
            var from = GeomFrom;
            if (include_endpoints) yield return from;

            var p = from;
            var ang_step = Angle / cnt;

            var ang = ang_step;
            var ax_rot = new Line3D(Center, CS.BaseZ, Line3DConstructMode.PointAndVector);

            for (int i = 0; i < cnt - 1; ++i)
            {
                p = from.RotateAboutAxis(ax_rot, ang);
                yield return p;

                ang += ang_step;
            }

            if (include_endpoints) yield return GeomTo;
        }

        /// <summary>
        /// compute wcs bbox executing a recursive bisect search of min and max
        /// </summary>
        public override BBox3D BBox(double tol)
        {
            return new BBox3D(new[] {
                    new Vector3D(
                        SearchOrd(tol, 0, AngleStart, AngleEnd, ltOrGt: true), // xmin
                        SearchOrd(tol, 1, AngleStart, AngleEnd, ltOrGt: true), // ymin
                        SearchOrd(tol, 2, AngleStart, AngleEnd, ltOrGt: true)), // zmin
                    new Vector3D(
                        SearchOrd(tol, 0, AngleStart, AngleEnd, ltOrGt: false), // xmax
                        SearchOrd(tol, 1, AngleStart, AngleEnd, ltOrGt: false), // ymax
                        SearchOrd(tol, 2, AngleStart, AngleEnd, ltOrGt: false)) // zmax
                });
        }

        public override IEnumerable<Geometry> GeomIntersect(double tol, Geometry _other,
            GeomSegmentMode thisSegmentMode, GeomSegmentMode otherSegmentMode)
        {
            switch (_other.GeomType)
            {
                case GeometryType.Line3D:
                    {
                        var other = (Line3D)_other;

                        var pts = this.Intersect(tol, other, onlyPerimeter: true,
                            segment_mode: otherSegmentMode == GeomSegmentMode.FromTo);

                        if (pts != null)
                        {
                            foreach (var pt in pts) yield return pt;
                        }
                    }
                    break;

                case GeometryType.Arc3D:
                    {
                        var other = (Arc3D)_other;

                        var pts = this.Intersect(tol, other, onlyPerimeter: true).ToList();

                        if (pts.Count > 0)
                        {
                            foreach (var pt in pts) yield return pt;
                        }
                    }
                    break;

                default: throw new NotImplementedException($"intersect with {this.GeomType} and {_other.GeomType}");
            }
        }

        public override EntityObject DxfEntity
        {
            get
            {
                var dxf_cs = new CoordinateSystem3D(CS.Origin, CS.BaseZ, CoordinateSystem3DAutoEnum.AAA);
                var astart = dxf_cs.BaseX.AngleToward(NormalizedLengthTolerance, From - CS.Origin, CS.BaseZ);
                var aend = dxf_cs.BaseX.AngleToward(NormalizedLengthTolerance, To - CS.Origin, CS.BaseZ);
                var arc = new Arc(Center, Radius, astart.ToDeg(), aend.ToDeg());
                arc.Normal = CS.BaseZ;
                return arc;
            }
        }

        #endregion        

        /// <summary>
        /// coordinate system centered in arc center
        /// angle is 0 at X axis
        /// angle increase rotating right-hand on Z axis
        /// </summary>            
        public CoordinateSystem3D CS { get; private set; }

        /// <summary>
        /// radius of arc
        /// </summary>            
        public double Radius { get; private set; }

        private double tol_rad;

        [JsonConstructor]
        Arc3D() : base(GeometryType.Arc3D)
        {
        }

        /// <summary>
        /// construct 3d arc
        /// </summary>
        /// <param name="cs">coordinate system with origin at arc center, XY plane of cs contains the arc, angle is 0 at cs x-axis and increase right-hand around cs z-axis</param>
        /// <param name="r">arc radius</param>
        /// <param name="angleRadStart">arc angle start (rad). is not required that start angle less than end. It will normalized 0-2pi</param>
        /// <param name="angleRadEnd">arc angle end (rad). is not require that end angle great than start. It will normalized 0-2pi</param>        
        /// <returns>3d arc</returns>
        public Arc3D(CoordinateSystem3D cs, double r, double angleRadStart, double angleRadEnd) :
            base(GeometryType.Arc3D)
        {
            AngleStart = angleRadStart.NormalizeAngle();
            AngleEnd = angleRadEnd.NormalizeAngle();
            CS = cs;
            Radius = r;
        }

        /// <summary>
        /// build a copy of given arc
        /// </summary>    
        public Arc3D(Arc3D arc) : base(GeometryType.Arc3D)
        {
            CopyFrom(arc);

            tol_rad = arc.tol_rad;
            AngleStart = arc.AngleStart;
            AngleEnd = arc.AngleEnd;
            CS = arc.CS;
            Radius = arc.Radius;
        }

        /// <summary>
        /// create an arc copy with origin moved
        /// </summary>
        /// <param name="delta">new arc origin delta</param>        
        public virtual Arc3D Move(Vector3D delta) => new Arc3D(CS.Move(delta), Radius, AngleStart, AngleEnd);

        /// <summary>
        /// helper to build circle by given 3 points
        /// </summary>
        /// <param name="p1">first constraint point</param>            
        /// <param name="p2">second constraint point</param>            
        /// <param name="p3">third constraint point</param>            
        /// <returns>simplified cs and radius that describe a 3d circle</returns>
        static (CoordinateSystem3D CS, double Radius) CircleBy3Points(Vector3D p1, Vector3D p2, Vector3D p3)
        {
            // https://en.wikipedia.org/wiki/Circumscribed_circle
            // Cartesian coordinates from cross- and dot-products

            var d = ((p1 - p2).CrossProduct(p2 - p3)).Length;

            var Radius = ((p1 - p2).Length * (p2 - p3).Length * (p3 - p1).Length) / (2 * d);

            var alpha = Pow((p2 - p3).Length, 2) * (p1 - p2).DotProduct(p1 - p3) / (2 * Pow(d, 2));
            var beta = Pow((p1 - p3).Length, 2) * (p2 - p1).DotProduct(p2 - p3) / (2 * Pow(d, 2));
            var gamma = Pow((p1 - p2).Length, 2) * (p3 - p1).DotProduct(p3 - p2) / (2 * Pow(d, 2));

            var c = alpha * p1 + beta * p2 + gamma * p3;

            var CS = new CoordinateSystem3D(c, p1 - c, p2 - c).Simplified();

            return (CS, Radius);
        }

        /// <summary>
        /// build 3d arc by given 3 points (angles 0,2pi)
        /// </summary>
        /// <param name="p1">first constraint point</param>
        /// <param name="p2">second constraint point</param>
        /// <param name="p3">third constraint point</param>
        /// <returns>3d arc passing for given points with angles 0-2pi</returns>
        protected Arc3D(Vector3D p1, Vector3D p2, Vector3D p3) :
            base(GeometryType.Arc3D)
        {
            GeomType = GeometryType.Arc3D;

            var nfo = Arc3D.CircleBy3Points(p1, p2, p3);

            CS = nfo.CS;
            Radius = nfo.Radius;

            AngleStart = 0;
            AngleEnd = 2 * PI;
        }

        /// <summary>
        /// Build arc by 3 given points.        
        /// Resulting simplified CS and normalized [0,2pi) AngleStart and AngleEnd will be choosen to meet follow requirements:
        /// fromPt = CS.Origin + (Radius * CS.BaseX).RotateAboutAxis(CS.BaseZ, AngleStart) ;
        /// toPt = CS.Origin + (Radius * CS.BaseX).RotateAboutAxis(CS.BaseZ, AngleEnd)
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="fromPt">arc start point</param>
        /// <param name="insidePt">arc inside point (ideally midpoint)</param>
        /// <param name="toPt">arc end point</param>        
        public Arc3D(double tol, Vector3D fromPt, Vector3D insidePt, Vector3D toPt) :
            base(GeometryType.Arc3D)
        {
            GeomType = GeometryType.Arc3D;

            var nfo = Arc3D.CircleBy3Points(fromPt, insidePt, toPt);

            CS = nfo.CS;
            Radius = nfo.Radius;

            // if (normal != null)
            // {
            //     if (!normal.Colinear(NormalizedLengthTolerance, CS.BaseZ)) throw new ArgumentException($"invalid given normal not colinear to arc axis");
            //     if (!normal.Concordant(NormalizedLengthTolerance, CS.BaseZ))
            //     {
            //         CS = CS.Rotate(CS.BaseX, PI);
            //     }
            // }

            var _start = CS.BaseX.AngleToward(tol, fromPt - CS.Origin, CS.BaseZ).NormalizeAngle();
            var _mid = CS.BaseX.AngleToward(tol, insidePt - CS.Origin, CS.BaseZ).NormalizeAngle();
            var _end = CS.BaseX.AngleToward(tol, toPt - CS.Origin, CS.BaseZ).NormalizeAngle();

            var cmp = new NormalizedAngleComparer(_start, _end);

            if (cmp.Compare(_mid, _start) > 0)// _mid.GreatThanOrEqualsTol(TwoPIRadTol, _start) && _mid.LessThanOrEqualsTol(TwoPIRadTol, _end))
            {
                AngleStart = _start;
                AngleEnd = _end;
            }
            else
            {
                CS = CS.FlipZ();

                AngleStart = _end;
                AngleEnd = _start;
            }
        }


        /// <summary>
        /// start angle (rad) [0-2pi) respect cs xaxis rotating around cs zaxis
        /// note that start angle can be greather than end angle
        /// </summary>
        public double AngleStart { get; protected set; }

        /// <summary>
        /// end angle (rad) [0-2pi) respect cs xaxis rotating around cs zaxis
        /// note that start angle can be greather than end angle
        /// </summary>            
        public double AngleEnd { get; protected set; }

        /// <summary>
        /// Arc (rad) angle length.
        /// angle between start-end or end-start depending on what start is less than end or not
        /// </summary>
        public double Angle => AngleStart.Angle(AngleEnd);

        /// <summary>
        /// point on the arc circumnfere at given angle (rotating cs basex around cs basez)
        /// note: it start
        /// </summary>
        public Vector3D PtAtAngle(double angleRad) => (Vector3D.XAxis * Radius).RotateAboutZAxis(angleRad).ToWCS(CS);

        /// <summary>
        /// return the angle (rad) of the point respect cs x axis rotating around cs z axis
        /// to reach given point angle alignment
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="pt">point to query angle respect csx axis for</param>        
        public double PtAngle(double tol, Vector3D pt)
        {
            var v_x = CS.BaseX;
            var v_pt = pt - CS.Origin;

            return v_x.AngleToward(tol, v_pt, CS.BaseZ);
        }

        /// <summary>
        /// point at angle start
        /// </summary>
        public Vector3D From => PtAtAngle(AngleStart);

        /// <summary>
        /// point at angle end
        /// </summary>            
        public Vector3D To => PtAtAngle(AngleEnd);

        /// <summary>
        /// return From,To segment
        /// </summary>
        public Line3D Segment => new Line3D(From, To);

        /// <summary>
        /// Checks if two arcs are equals ( it checks agains swapped from-to too )
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="other">other arc</param>
        /// <returns>trus if two arcs equals</returns>
        public bool EqualsTol(double tol, Arc3D other)
        {
            if (!Center.EqualsTol(tol, other.Center)) return false;
            if (!Radius.EqualsTol(tol, other.Radius)) return false;
            if (!Segment.EqualsTol(tol, other.Segment)) return false;
            return true;
        }

        /// <summary>
        /// http://www.lee-mac.com/bulgeconversion.html
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="from">buldge from pt</param>
        /// <param name="to">buldge to pt</param>
        /// <param name="N">arc normal</param>
        /// <returns>arc buldge value</returns>
        public double Bulge(double tol) =>
            (Sense ? 1d : -1d) * Tan((GeomFrom - Center).AngleToward(tol, GeomTo - Center, CS.BaseZ) / 4);

        /// <summary>
        /// statis if given point contained in arc perimeter/shape or circle perimeter/shape depending on specified mode
        /// </summary>                
        /// <param name="tol">len tolerance</param>
        /// <param name="pt">point to test</param>
        /// <param name="inArcAngleRange">true if point angle must contained in arc angles, false to test like a circle</param>
        /// <param name="onlyPerimeter">true to test point contained only in perimeter, false to test also contained in area</param>
        /// <returns>true if arc contains given pt</returns>        
        protected bool Contains(double tol, Vector3D pt,
            bool inArcAngleRange, bool onlyPerimeter)
        {
            var onplane = pt.ToUCS(CS).Z.EqualsTol(tol, 0);
            var center_dst = pt.Distance(CS.Origin);

            if (inArcAngleRange)
            {
                var ptAngle = PtAngle(tol, pt);
                var isInAngleRange = ptAngle.AngleInRange(AngleStart, AngleEnd, radTol: tol.RadTol(Radius));
                if (!isInAngleRange) return false;
            }

            if (onlyPerimeter)
                return onplane && center_dst.EqualsTol(tol, Radius);
            else
                return onplane && center_dst.LessThanOrEqualsTol(tol, Radius);
        }

        /// <summary>
        /// states if given point relies on this arc perimeter or shape depending on arguments
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="pt">point to check</param>
        /// <param name="onlyPerimeter">if true it checks if point is on perimeter; if false it will check in area too</param>
        public virtual bool Contains(double tol, Vector3D pt, bool onlyPerimeter) =>
            Contains(tol, pt, true, onlyPerimeter);

        public Vector3D Center => CS.Origin;

        public Circle3D ToCircle3D(double tol) => new Circle3D(CS, Radius);

        /// <summary>
        /// Area of circular sector
        /// </summary>
        public double CircularSectorArea => PI * Pow(Radius, 2) * Angle / (2 * PI);

        /// <summary>
        /// Area of chord triangle
        /// </summary>        
        public double ChordTriangleArea => .5 * Pow(Radius, 2) * Sin(Angle);

        /// <summary>
        /// Segment area ( CircularSectorArea - ChordTriangleArea )
        /// </summary>
        public double SegmentArea => CircularSectorArea - ChordTriangleArea;

        /// <summary>
        /// centre of mass of circular segment
        /// </summary>
        /// <param name="A">arc area</param>
        /// <returns>location of arc centre of mass</returns>
        public Vector3D CentreOfMass(out double A)
        {
            // https://en.wikipedia.org/wiki/List_of_centroids

            var alpha = Angle / 2;
            A = Pow(Radius, 2) / 2 * (2 * alpha - Sin(2 * alpha));

            var x = (4 * Radius * Pow(Sin(alpha), 3)) / (3 * (2 * alpha - Sin(2 * alpha)));

            return Center + (MidPoint - Center).Normalized() * x;
        }

        private double SearchOrd(double tol, int ord, double angleFrom, double angleTo, bool ltOrGt)
        {
            var ang = angleFrom.Angle(angleTo);

            var fromVal = PtAtAngle(angleFrom).GetOrd(ord);
            var midVal = PtAtAngle(angleFrom + ang / 2).GetOrd(ord);
            var toVal = PtAtAngle(angleTo).GetOrd(ord);

            if (ltOrGt)
            {
                if (fromVal.LessThanTol(tol, toVal))
                {
                    if (fromVal.EqualsTol(tol, midVal)) return (fromVal + midVal) / 2;
                    return SearchOrd(tol, ord, angleFrom, angleFrom + ang / 2, ltOrGt);
                }
                else // to < from
                {
                    if (midVal.EqualsTol(tol, toVal)) return (midVal + toVal) / 2;
                    return SearchOrd(tol, ord, angleFrom + ang / 2, angleTo, ltOrGt);
                }
            }
            else
            {
                if (fromVal.GreatThanTol(tol, toVal))
                {
                    if (fromVal.EqualsTol(tol, midVal)) return fromVal;
                    return SearchOrd(tol, ord, angleFrom, angleFrom + ang / 2, ltOrGt);
                }
                else // to < from
                {
                    if (midVal.EqualsTol(tol, toVal)) return toVal;
                    return SearchOrd(tol, ord, angleFrom + ang / 2, angleTo, ltOrGt);
                }
            }
        }

        /// <summary>        
        /// intersect this 3d circle with given 3d line        
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="l">line</param>
        /// <param name="segment_mode">consider line as segment instead of infinite</param>
        /// <returns>intersection points between this circle and given line, can be at most 2 points</returns>
        private IEnumerable<Vector3D> IntersectCircle(double tol, Line3D l, bool segment_mode = false)
        {
            var lprj = new Line3D(l.From.ToUCS(CS).Set(OrdIdx.Z, 0), l.To.ToUCS(CS).Set(OrdIdx.Z, 0));

            var a = Pow(lprj.To.X - lprj.From.X, 2) + Pow(lprj.To.Y - lprj.From.Y, 2);
            var b = 2 * lprj.From.X * (lprj.To.X - lprj.From.X) + 2 * lprj.From.Y * (lprj.To.Y - lprj.From.Y);
            var c = Pow(lprj.From.X, 2) + Pow(lprj.From.Y, 2) - Pow(Radius, 2);
            var d = Pow(b, 2) - 4 * a * c;

            if (d.LessThanTol(tol, 0)) yield break; // no intersection at all

            var sd = Sqrt(Abs(d));
            var f1 = (-b + sd) / (2 * a);
            var f2 = (-b - sd) / (2 * a);

            // one intersection point is
            var ip = new Vector3D(
                lprj.From.X + (lprj.To.X - lprj.From.X) * f1,
                lprj.From.Y + (lprj.To.Y - lprj.From.Y) * f1,
                0);

            Vector3D? ip2 = null;

            if (!f1.EqualsTol(NormalizedLengthTolerance, f2))
            {
                // second intersection point is
                ip2 = new Vector3D(
                    lprj.From.X + (lprj.To.X - lprj.From.X) * f2,
                    lprj.From.Y + (lprj.To.Y - lprj.From.Y) * f2,
                    0);
            }

            // back to wcs, check line contains point
            var wcs_ip = ip.ToWCS(CS);

            if (l.LineContainsPoint(tol, wcs_ip, segment_mode))
                yield return wcs_ip;

            if (ip2 != null)
            {
                var wcs_ip2 = ip2.ToWCS(CS);

                if (ip2 != null && l.LineContainsPoint(tol, wcs_ip2, segment_mode))
                    yield return wcs_ip2;
            }
        }

        /// <summary>
        /// finds intersection points between two arcs
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="other">other arc</param>
        /// <param name="onlyPerimeter">true to test point contained only in perimeter, false to test also contained in area</param>
        /// <returns></returns>
        public IEnumerable<Vector3D> Intersect(double tol, Arc3D other, bool onlyPerimeter = true)
        {
            var c1 = this.ToCircle3D(tol);
            var c2 = other.ToCircle3D(tol);

            var pts = c1.Intersect(tol, c2).ToList();

            return pts.Where(ip =>
                this.Contains(tol, ip, inArcAngleRange: true, onlyPerimeter) &&
                other.Contains(tol, ip, inArcAngleRange: true, onlyPerimeter));
        }

        /// <summary>
        /// states if this arc intersect given line
        /// </summary>
        /// <param name="tol">arc tolerance</param>
        /// <param name="l">line to test intersect</param>
        /// <param name="segment_mode">if true line treat as segment instead of infinite</param>            
        /// <param name="only_perimeter">check intersection only along perimeter; if false it will check intersection along arc area shape border too</param>
        /// <param name="circle_mode">if true arc treat as circle</param>            
        protected IEnumerable<Vector3D> Intersect(double tol, Line3D l,
            bool only_perimeter,
            bool segment_mode,
            bool circle_mode)
        {
            var cmp = new Vector3DEqualityComparer(tol);
            var res = new HashSet<Vector3D>(cmp);

            foreach (var x in IntersectCircle(tol, l, segment_mode))
            {
                res.Add(x);
            }

            if (!only_perimeter)
            {
                var c_f = new Line3D(Center, From);
                {
                    var q_c_f = c_f.Intersect(tol, l);
                    if (q_c_f != null) res.Add(q_c_f);
                }

                if (!circle_mode)
                {
                    var c_e = new Line3D(Center, To);
                    {
                        var q_c_e = c_e.Intersect(tol, l);
                        if (q_c_e != null) res.Add(q_c_e);
                    }
                }
            }

            if (!circle_mode)
                return res.Where(r => this.Contains(tol, r, onlyPerimeter: only_perimeter));

            return res;
        }

        /// <summary>
        /// find ips of intersection between this arc and given line
        /// </summary>
        /// <param name="tol">length tolerance</param>
        /// <param name="l">line</param>
        /// <param name="onlyPerimeter">check intersection only along perimeter; if false it will check intersection along arc area shape border too</param>
        /// <param name="segment_mode">if true treat given line as segment; if false as infinite line</param>
        /// <returns>intersection points between this arc and given line</returns>
        public virtual IEnumerable<Vector3D> Intersect(double tol, Line3D l,
            bool onlyPerimeter = true, bool segment_mode = false) => Intersect(tol, l,
                only_perimeter: onlyPerimeter,
                segment_mode: segment_mode,
                circle_mode: false);

        /// <summary>
        /// find ips of intersect this arc to the given cs plane; 
        /// return empty set if arc cs plane parallel to other given cs
        /// </summary>            
        /// <param name="tol">len tolerance</param>
        /// <param name="cs">cs xy plane</param>
        /// <param name="only_perimeter">if false it will check in the arc area too, otherwise only on arc perimeter</param>
        /// <returns>sample</returns>
        /// <remarks>            
        /// [unit test](https://github.com/devel0/netcore-sci/tree/master/test/Arc3D/Arc3DTest_0001.cs)
        /// ![image](../test/Arc3D/Arc3DTest_0001.png)
        /// </remarks>            
        public IEnumerable<Vector3D> Intersect(double tol, CoordinateSystem3D cs,
            bool only_perimeter = true)
        {
            if (this.CS.IsParallelTo(tol, cs)) yield break;

            var iLine = this.CS.Intersect(tol, cs);

            foreach (var x in this.Intersect(tol, iLine,
                only_perimeter: only_perimeter,
                segment_mode: false,
                circle_mode: false))
                yield return x;
        }

        public override string ToString() => ToString(digits: 3);

        public string ToString(int digits = 3) =>
            $"[{GetType().Name}]{((!Sense) ? " !S" : "")} L:{Round(Length, 2)} SFROM[{SGeomFrom.ToString(digits)}] STO[{SGeomTo.ToString(digits)}] MID[{MidPoint.ToString(digits)}] C:{Center} r:{Round(Radius, 3)} ANGLE:{Round(Angle.ToDeg(), 1)}deg ({Round(AngleStart.ToDeg(), 1)}->{Round(AngleEnd.ToDeg(), 1)})";

        /// <summary>
        /// create a set of subarc from this by splitting through given split points
        /// split point are not required to be on perimeter of the arc ( a center arc to point line will split )
        /// generated subarcs will start from this arc angleFrom and contiguosly end to angleTo
        /// </summary>                        
        /// <param name="tol">arc length tolerance</param>
        /// <param name="_splitPts">point where split arc</param>
        /// <param name="validate_pts">if true split only for split points on arc perimeter</param>            
        public IEnumerable<Arc3D> Split(double tol, IEnumerable<Vector3D> _splitPts, bool validate_pts = false)
        {
            if (_splitPts == null || _splitPts.Count() == 0) yield break;

            IEnumerable<Vector3D> splitPts = _splitPts;

            if (validate_pts) splitPts = _splitPts.Where(pt => Contains(tol, pt, onlyPerimeter: true)).ToList();

            var radCmp = new DoubleEqualityComparer(TwoPIRadTol);

            var hs_angles_rad = new HashSet<double>(radCmp);
            foreach (var splitPt in splitPts.Select(pt => PtAngle(tol, pt)))
            {
                if (PtAtAngle(splitPt).EqualsTol(tol, From) || PtAtAngle(splitPt).EqualsTol(tol, To)) continue;
                hs_angles_rad.Add(splitPt.NormalizeAngle());
            }

            var angles_rad = hs_angles_rad.OrderBy(w => w).ToList();
            if (!hs_angles_rad.Contains(AngleStart)) angles_rad.Insert(0, AngleStart);
            if (!hs_angles_rad.Contains(AngleEnd)) angles_rad.Add(AngleEnd);

            for (int i = 0; i < angles_rad.Count - 1; ++i)
            {
                var arc = new Arc3D(CS, Radius, angles_rad[i], angles_rad[i + 1]);
                yield return arc;
            }
        }

    }

    public static partial class SciExt
    {

        /// <summary>
        /// compute angle rad tolerance by given arc length tolerance as (lenTol / radius)
        /// </summary>
        /// <param name="lenTol">length tolerance on the arc</param>
        /// <param name="radius">radius of the arc</param>
        public static double RadTol(this double lenTol, double radius) => lenTol / radius;

        /// <summary>
        /// construct arc3 from given dxf arc
        /// </summary>
        /// <param name="dxf_arc"></param>        
        public static Arc3D ToArc3D(this netDxf.Entities.Arc dxf_arc) => new Arc3D(
            new CoordinateSystem3D(dxf_arc.Center, dxf_arc.Normal, CoordinateSystem3DAutoEnum.AAA),
            dxf_arc.Radius,
            dxf_arc.StartAngle.ToRad(), dxf_arc.EndAngle.ToRad());

        /// <summary>
        /// states if given angle is contained in from, to angle range;
        /// multiturn angles are supported because test will normalize to [0,2pi) automatically.
        /// </summary>                
        /// <param name="pt_angle">angle(rad) to test</param>        
        /// <param name="angle_from">angle(rad) from</param>
        /// <param name="angle_to">angle(rad) to</param>        
        /// <param name="radTol">optional rad tolerance</param>  
        public static bool AngleInRange(this double pt_angle, double angle_from, double angle_to, double radTol = TwoPIRadTol)
        {
            pt_angle = pt_angle.NormalizeAngle(radTol: radTol);
            angle_from = angle_from.NormalizeAngle(radTol: radTol);
            angle_to = angle_to.NormalizeAngle(radTol: radTol);

            if (angle_from.GreatThanTol(radTol, angle_to))
            {
                return
                    pt_angle.LessThanOrEqualsTol(radTol, angle_to)
                    ||
                    pt_angle.GreatThanOrEqualsTol(radTol, angle_from);
            }
            else // from < to
            {
                return
                    pt_angle.GreatThanOrEqualsTol(radTol, angle_from)
                    &&
                    pt_angle.LessThanOrEqualsTol(radTol, angle_to);
            }
        }

    }

    /// <summary>
    /// checks if arcs share same plane, origin, radius, angle start-end
    /// </summary>
    public class Arc3DEqualityComparer : IEqualityComparer<Arc3D>
    {
        /// <summary>
        /// length tolerance
        /// </summary>
        double tol;

        /// <summary>
        /// arc 3d eq comparer
        /// </summary>
        /// <param name="_tol">length tolerance</param>
        public Arc3DEqualityComparer(double _tol)
        {
            tol = _tol;
        }

        public bool Equals(Arc3D? x, Arc3D? y)
        {
            if (x == null || y == null) return false;

            return x.EqualsTol(tol, y);
        }

        public int GetHashCode(Arc3D obj) => 0;

    }

    //     public static class SciToolkit
    //     {

    // public static readonly NormalizedAngleComparer TwoPiNormalizedAngleComparer =
    // new NormalizedAngleComparer(TwoPIRadTol

    //     }

}