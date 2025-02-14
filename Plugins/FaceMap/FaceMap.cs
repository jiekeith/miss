
extern alias Drawing;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews;
using MissionPlanner.Maps;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace MissionPlanner
{
    public class FaceMap
    {
        // 常量：弧度与角度的转换系数
        const double rad2deg = (180 / Math.PI);
        const double deg2rad = (1.0 / rad2deg);

        // 结构体：表示一条线的起点、终点和基准点
        public struct Linelatlng
        {
            // 线的起点
            public utmpos p1;
            // 线的终点
            public utmpos p2;
            // 用于沿线的网格基准点（初始设置）
            public utmpos basepnt;
        }

        // 静态变量：表示起始点的经纬度和高度
        public static PointLatLngAlt StartPointLatLngAlt = PointLatLngAlt.Zero;

        // 静态方法：将一条线添加到地图上
        static void Addtomap(Linelatlng pos)
        {
            // 创建一个包含起点和终点的点列表
            List<PointLatLng> list = new List<PointLatLng>
            {
                pos.p1.ToLLA(),
                pos.p2.ToLLA()
            };

            // 将线添加到地图上的多边形覆盖层中
            polygons.Routes.Add(new GMapRoute(list, "test") { Stroke = new System.Drawing.Pen(System.Drawing.Color.Yellow, 4) });
        }

        // 静态方法：将UTM位置点添加到地图上（调试用）
        static void Addtomap(utmpos pos, string tag)
        {
            // 将UTM位置点转换为经纬度并添加到地图上的标记覆盖层中
            polygons.Markers.Add(new GMapMarkerWP(pos.ToLLA(), tag));
        }

        // 静态方法：缩放并居中地图
        static void Zoomandcentermap()
        {
            // 缩放并居中地图上的标记
            map.ZoomAndCenterMarkers("polygons");

            // 强制刷新地图
            map.Invalidate();

            // 停止计时器
            timer.Stop();
        }

        // 静态变量：地图覆盖层、地图控件和计时器
        static GMapOverlay polygons = new GMapOverlay("polygons");
        static myGMAP map = new myGMAP();
        static Timer timer = new Timer();

        // 静态方法：调试功能，初始化地图并显示
        static void DoDebug()
        {
            // 清除多边形覆盖层
            polygons.Clear();

            // 设置计时器间隔并绑定事件
            timer.Interval = 2000;
            timer.Tick += (sender, args) => { Zoomandcentermap(); };
            timer.Start();

            // 如果地图控件已经创建，则返回
            if (map.IsHandleCreated)
                return;

            // 初始化地图控件
            polygons = new GMapOverlay("polygons");
            map = new myGMAP
            {
                MapProvider = GMapProviders.GoogleSatelliteMap,
                MaxZoom = 24
            };
            map.Overlays.Add(polygons);
            map.Size = new Size(1024, 768);
            map.Dock = DockStyle.Fill;

            // 显示地图控件
            map.ShowUserControl();
        }

        // 静态方法：创建飞行路径
        public static List<PointLatLngAlt> CreateCorridor(List<PointLatLngAlt> polygon, double height, double camViewHeight, double camVertSpacing, double distance, double angle,
            double camPitch, bool flipDirection, double bermDepth, int numBenches, double toeHeight, double toepoint, double toepoint_runs, bool pathHome, double homeAlt, FlightPlanner.altmode altmode)
        {
            // 根据方向标志设置方向
            int direction = (flipDirection == true ? -1 : 1);

            // 确保垂直间距不小于0.1
            if (camVertSpacing < 0.1)
                camVertSpacing = 0.1;

            // 如果多边形为空或台阶数小于1，返回空列表
            if (polygon.Count == 0 || numBenches < 1)
                return new List<PointLatLngAlt>();

            // 初始化结果列表
            List<PointLatLngAlt> ans = new List<PointLatLngAlt>();

            // 获取多边形的UTM区域
            int utmzone = polygon[0].GetUTMZone();

            // 将多边形点转换为UTM坐标
            List<utmpos> utmpositions = utmpos.ToList(PointLatLngAlt.ToUTM(utmzone, polygon), utmzone);

            // 初始化垂直和水平偏移量
            double vertOffset = 0;
            double horizOffset = 0;
            double toepoint_runs_count = 0;

            // 计算初始高度
            double initialAltitude = toepoint;
            var vertIncrement = camVertSpacing * Math.Sin(angle * deg2rad);
            var lanes = Math.Round((height - initialAltitude) / vertIncrement) + toepoint_runs + 1;

            // 遍历每个台阶
            for (int bench = 0; bench < numBenches; bench++)
            {
                // 遍历每个高度增量
                for (int lane = 0; lane < lanes; lane++)
                {
                    // 如果还在处理toepoint_runs
                    if (toepoint_runs_count < toepoint_runs)
                    {
                        // 计算垂直和水平偏移量
                        vertOffset = distance * Math.Sin(camPitch * deg2rad) + (initialAltitude + (bench * height) + toeHeight);
                        horizOffset = distance * Math.Cos(camPitch * deg2rad) - ((initialAltitude) / Math.Tan(angle * deg2rad)) - bench * (bermDepth + height / Math.Tan(angle * deg2rad));
                        toepoint_runs_count++;
                    }
                    else
                    {
                        // 计算垂直和水平偏移量
                        vertOffset = distance * Math.Sin(camPitch * deg2rad) + (initialAltitude + ((lane - toepoint_runs) * vertIncrement) + (bench * height) + toeHeight);
                        horizOffset = distance * Math.Cos(camPitch * deg2rad) - ((initialAltitude + ((lane - toepoint_runs) * vertIncrement)) / Math.Tan(angle * deg2rad)) - bench * (bermDepth + height / Math.Tan(angle * deg2rad));
                    }

                    // 如果是第一个高度增量，添加中间点
                    if (lane == 0 && ans.Count > 0)
                    {
                        PointLatLngAlt intermediateWP = new PointLatLngAlt(ans.Last().Lat, ans.Last().Lng, vertOffset)
                        {
                            Tag = "S"
                        };
                        ans.Add(intermediateWP);
                    }

                    // 生成偏移路径并添加到结果列表
                    GenerateOffsetPath(utmpositions, horizOffset * direction, utmzone)
                        .ForEach(pnt => { ans.Add(pnt); ans.Last().Alt = vertOffset; });

                    // 反转UTM位置列表和方向
                    utmpositions.Reverse();
                    direction = -direction;
                }
            }

            // 如果需要返回起点，生成最后一条路径
            if (pathHome && ((lanes * numBenches) % 2) == 1)
            {
                GenerateOffsetPath(utmpositions, horizOffset * direction, utmzone)
                    .ForEach(pnt => { ans.Add(pnt); ans.Last().Alt = vertOffset; ans.Last().Tag = "R"; });
            }
            return ans;
        }

        // 静态方法：生成偏移路径
        private static List<utmpos> GenerateOffsetPath(List<utmpos> utmpositions, double distance, int utmzone)
        {
            List<utmpos> ans = new List<utmpos>();

            utmpos oldpos = utmpos.Zero;

            // 遍历UTM位置列表
            for (int a = 0; a < utmpositions.Count - 2; a++)
            {
                var prevCenter = utmpositions[a];
                var currCenter = utmpositions[a + 1];
                var nextCenter = utmpositions[a + 2];

                // 计算前后两条线的方位角
                var l1bearing = prevCenter.GetBearing(currCenter);
                var l2bearing = currCenter.GetBearing(nextCenter);

                // 计算偏移后的点
                var l1prev = Newpos(prevCenter, l1bearing + 90, distance);
                var l1curr = Newpos(currCenter, l1bearing + 90, distance);

                var l2curr = Newpos(currCenter, l2bearing + 90, distance);
                var l2next = Newpos(nextCenter, l2bearing + 90, distance);

                // 计算两条偏移线的交点
                var l1l2center = FindLineIntersectionExtension(l1prev, l1curr, l2curr, l2next);

                // 如果是第一个点，添加起点
                if (a == 0)
                {
                    l1prev.Tag = "S";
                    ans.Add(l1prev);

                    l1prev.Tag = "SM";
                    ans.Add(l1prev);

                    oldpos = l1prev;
                }

                // 添加中间点
                l1l2center.Tag = "M";
                ans.Add(l1l2center);
                oldpos = l1l2center;

                // 如果是最后一个点，添加终点
                if ((a + 3) == utmpositions.Count)
                {
                    l2next.Tag = "ME";
                    ans.Add(l2next);

                    l2next.Tag = "E";
                    ans.Add(l2next);
                }
            }

            return ans;
        }

        // 静态方法：极坐标转直角坐标
        static void Newpos(ref double x, ref double y, double bearing, double distance)
        {
            double degN = 90 - bearing;
            if (degN < 0)
                degN += 360;
            x = x + distance * Math.Cos(degN * deg2rad);
            y = y + distance * Math.Sin(degN * deg2rad);
        }

        // 静态方法：极坐标转直角坐标
        static utmpos Newpos(utmpos input, double bearing, double distance)
        {
            double degN = 90 - bearing;
            if (degN < 0)
                degN += 360;
            double x = input.x + distance * Math.Cos(degN * deg2rad);
            double y = input.y + distance * Math.Sin(degN * deg2rad);

            return new utmpos(x, y, input.zone);
        }

        // 静态方法：计算两条线的交点
        public static utmpos FindLineIntersectionExtension(utmpos start1, utmpos end1, utmpos start2, utmpos end2)
        {
            double denom = ((end1.x - start1.x) * (end2.y - start2.y)) - ((end1.y - start1.y) * (end2.x - start2.x));
            // 如果两条线平行，返回零向量
            if (denom == 0)
                return utmpos.Zero;
            double numer = ((start1.y - start2.y) * (end2.x - start2.x)) -
                           ((start1.x - start2.x) * (end2.y - start2.y));
            double r = numer / denom;
            double numer2 = ((start1.y - start2.y) * (end1.x - start1.x)) -
                            ((start1.x - start2.x) * (end1.y - start1.y));
            double s = numer2 / denom;
            if ((r < 0 || r > 1) || (s < 0 || s > 1))
            {
                // 交点在线段之外
            }
            // 计算交点
            utmpos result = new utmpos
            {
                x = start1.x + (r * (end1.x - start1.x)),
                y = start1.y + (r * (end1.y - start1.y)),
                zone = start1.zone
            };
            return result;
        }
    }
}