﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Zip;
using System.Threading;
using System.Collections;
using System.Net.Http;
using System.Threading.Tasks;
using log4net;
using GMap.NET;

namespace MissionPlanner.Utilities
{
    public class srtm : IDisposable
    {
        private static readonly ILog log =
            LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public enum tiletype
        {
            valid,
            invalid,
            ocean
        }

        public class altresponce
        {
            public static readonly altresponce Invalid = new altresponce() {currenttype = tiletype.invalid, altsource = "Invalid"};
            public static readonly altresponce Ocean = new altresponce() {currenttype = tiletype.ocean, altsource = "Ocean"};

            public tiletype currenttype = tiletype.invalid;
            public double alt = 0;
            public string altsource = "";
        }

        private static string _datadirectory = "./srtm/";

        public static string datadirectory
        {
            get { return _datadirectory; }
            set
            {
                log.Info(value);
                _datadirectory = value;
            }
        }

        static object objlock = new object();
        static object extract = new object();

        static Thread requestThread;

        static bool requestThreadrun = false;

        static List<string> queue = new List<string>();

        static Hashtable filecache = new Hashtable();

        static List<string> oceantile = new List<string>();

        static Dictionary<string, short[,]> cache = new Dictionary<string, short[,]>();


        static Dictionary<int, string> _filenameDictionary = new Dictionary<int, string>();

        private static Func<int, int, string> filenameDictionary = (x, y) =>
        {
            int id = y * 1000 + x;
            lock (_filenameDictionary)
                if (_filenameDictionary.ContainsKey(id))
                    return _filenameDictionary[id];

            if (y < -90 || y > 90)
                return "";

            if (x < -180 || x > 180)
                return "";

            var sy = Math.Abs(y).ToString("00");

            var sx = Math.Abs(x).ToString("000");

            lock (_filenameDictionary)
                _filenameDictionary[id] = string.Format("{0}{1}{2}{3}{4}", y >= 0 ? "N" : "S", sy,
                    x >= 0 ? "E" : "W", sx, ".hgt");

            return _filenameDictionary[id];
        };

        static srtm()
        {
            log.Info(".cctor");

            if (!String.IsNullOrEmpty(Settings.Instance.UserAgent))
                client.DefaultRequestHeaders.Add("User-Agent", Settings.Instance.UserAgent);

            StartQueueProcess();
        }

        static string GetFilename(double lat, double lng)
        {
            int x = /*(lng < 0) ? (int) (lng - 1) : (int) lng; */(int)Math.Floor(lng);
            int y = /*(lat < 0) ? (int) (lat - 1) : (int) lat; */(int)Math.Floor(lat);

            int id = y*1000 + x;

            string filename = filenameDictionary(x, y);

            return filename;
        }

        public static altresponce getAltitude(double lat, double lng, double zoom = 16)
        {
            short alt = 0;

            try
            {
                var trytiff = Utilities.GeoTiff.getAltitude(lat, lng);

                if (trytiff.currenttype == tiletype.valid)
                    return trytiff;
            }
            catch (FileNotFoundException)
            {

            }
            catch (Exception ex)
            {
                log.Error(ex);
            }

            try
            {
                var trydted = Utilities.DTED.getAltitude(lat, lng);

                if (trydted.currenttype == tiletype.valid)
                    return trydted;
            }
            catch (FileNotFoundException)
            {

            }
            catch (Exception ex)
            {
                log.Error(ex);
            }

            //lat += 1 / 1199.0;
            //lng -= 1 / 1201f;

            // 		lat	-35.115676879882812	double
            //		lng	117.94178754638671	double
            // 		alt	70	short

            var filename = GetFilename(lat, lng);

            if (String.IsNullOrEmpty(filename))
                return altresponce.Invalid;

            try
            {
                // marked as a oceantile
                if (oceantile.Contains(filename))
                    return altresponce.Ocean;

                // is it in our dl queue
                lock (objlock)
                {
                    if (queue.Contains(filename))
                    {
                        return altresponce.Invalid;
                    }
                }

                if (cache.ContainsKey(filename) || File.Exists(datadirectory + Path.DirectorySeparatorChar + filename))
                {
                    // srtm hgt files

                    int size = -1;

                    // add to cache
                    if (!cache.ContainsKey(filename))
                    {
                        lock (extract)
                        {
                            Thread.Sleep(0);
                        }

                        using (
                            FileStream fs = new FileStream(datadirectory + Path.DirectorySeparatorChar + filename,
                                FileMode.Open, FileAccess.Read, FileShare.Read))
                        {

                            if (fs.Length == (1201 * 1201 * 2))
                            {
                                size = 1201;
                            }
                            else if (fs.Length == (3601 * 3601 * 2))
                            {
                                size = 3601;
                            }
                            else
                                return srtm.altresponce.Invalid;

                            byte[] altbytes = new byte[2];
                            short[,] altdata = new short[size, size];


                            int altlat = 0;
                            int altlng = 0;

                            while (fs.Read(altbytes, 0, 2) != 0)
                            {
                                altdata[altlat, altlng] = (short) ((altbytes[0] << 8) + altbytes[1]);

                                altlat++;
                                if (altlat >= size)
                                {
                                    altlng++;
                                    altlat = 0;
                                }
                            }

                            lock(cache)
                                cache[filename] = altdata;
                        }
                    }

                    if (cache[filename].Length == (1201 * 1201))
                    {
                        size = 1201;
                    }
                    else if (cache[filename].Length == (3601 * 3601))
                    {
                        size = 3601;
                    }
                    else
                        return srtm.altresponce.Invalid;

                    int x = /*(lng < 0) ? (int) (lng - 1) : (int) lng; */(int) Math.Floor(lng);
                    int y = /*(lat < 0) ? (int) (lat - 1) : (int) lat; */(int) Math.Floor(lat);

                    // remove the base lat long
                    lat -= y;
                    lng -= x;

                    // values should be 0-1199, 1200 is for interpolation
                    double xf = lng * (size - 1);
                    double yf = lat * (size - 1);

                    int x_int = (int) xf;
                    double x_frac = xf - x_int;

                    int y_int = (int) yf;
                    double y_frac = yf - y_int;

                    y_int = (size - 2) - y_int;

                    double alt00 = GetAlt(filename, x_int, y_int);
                    double alt10 = GetAlt(filename, x_int + 1, y_int);
                    double alt01 = GetAlt(filename, x_int, y_int + 1);
                    double alt11 = GetAlt(filename, x_int + 1, y_int + 1);

                    double v1 = avg(alt00, alt10, x_frac);
                    double v2 = avg(alt01, alt11, x_frac);
                    double v = avg(v1, v2, 1 - y_frac);

                    if (v < -1000)
                        return altresponce.Invalid;

                    return new altresponce()
                    {
                        currenttype = tiletype.valid,
                        alt = v,
                        altsource = "SRTM"
                    };
                }

                string filename2 = "srtm_" + Math.Round((lng + 2.5 + 180) / 5, 0).ToString("00") + "_" +
                                   Math.Round((60 - lat + 2.5) / 5, 0).ToString("00") + ".asc";

                if (File.Exists(datadirectory + Path.DirectorySeparatorChar + filename2))
                {
                    using (
                        StreamReader sr =
                            new StreamReader(readFile(datadirectory + Path.DirectorySeparatorChar + filename2)))
                    {

                        int nox = 0;
                        int noy = 0;
                        float left = 0;
                        float top = 0;
                        int nodata = -9999;
                        float cellsize = 0;

                        int rowcounter = 0;

                        float wantrow = 0;
                        float wantcol = 0;


                        while (!sr.EndOfStream)
                        {
                            string line = sr.ReadLine();

                            if (line.StartsWith("ncols"))
                            {
                                nox = int.Parse(line.Substring(line.IndexOf(' ')));

                                //hgtdata = new int[nox * noy];
                            }
                            else if (line.StartsWith("nrows"))
                            {
                                noy = int.Parse(line.Substring(line.IndexOf(' ')));

                                //hgtdata = new int[nox * noy];
                            }
                            else if (line.StartsWith("xllcorner"))
                            {
                                left = float.Parse(line.Substring(line.IndexOf(' ')));
                            }
                            else if (line.StartsWith("yllcorner"))
                            {
                                top = float.Parse(line.Substring(line.IndexOf(' ')));
                            }
                            else if (line.StartsWith("cellsize"))
                            {
                                cellsize = float.Parse(line.Substring(line.IndexOf(' ')));
                            }
                            else if (line.StartsWith("NODATA_value"))
                            {
                                nodata = int.Parse(line.Substring(line.IndexOf(' ')));
                            }
                            else
                            {
                                string[] data = line.Split(new char[] {' '});

                                if (data.Length == (nox + 1))
                                {
                                    wantcol = (float) ((lng - Math.Round(left, 0)));

                                    wantrow = (float) ((lat - Math.Round(top, 0)));

                                    wantrow = (int) (wantrow / cellsize);
                                    wantcol = (int) (wantcol / cellsize);

                                    wantrow = noy - wantrow;

                                    if (rowcounter == wantrow)
                                    {
                                        Console.WriteLine("{0} {1} {2} {3} ans {4} x {5}", lng, lat, left, top,
                                            data[(int) wantcol], (nox + wantcol * cellsize));

                                        return new altresponce()
                                        {
                                            currenttype = tiletype.valid,
                                            alt = int.Parse(data[(int) wantcol])
                                        };
                                    }

                                    rowcounter++;
                                }
                            }
                        }
                    }

                    return new altresponce()
                    {
                        currenttype = tiletype.valid,
                        alt = alt,
                        altsource = "ASCII"
                    };
                }
                else // get something
                {
                    if (zoom >= 7)
                    {
                        if (!Directory.Exists(datadirectory))
                            Directory.CreateDirectory(datadirectory);

                        if (GMaps.Instance.Mode != AccessMode.CacheOnly)
                        {
                            lock (objlock)
                            {
                                if (!queue.Contains(filename))
                                {
                                    log.Info("Getting " + filename);
                                    queue.Add(filename);
                                    requestSemaphore.Release();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return altresponce.Invalid;
            }

            return altresponce.Invalid;
        }

        private static void StartQueueProcess()
        {
            requestRunner();
        }

        static double GetAlt(string filename, int x, int y)
        {
            return cache[filename][x, y];
        }

        static double avg(double v1, double v2, double weight)
        {
            return v2*weight + v1*(1 - weight);
        }

        public static PointLatLngAlt getIntersectionWithTerrain(PointLatLngAlt start, PointLatLngAlt end)
        {
            int distout = 0;
            int stepsize = 50;
            var maxdist = start.GetDistance(end);
            var bearing = start.GetBearing(end);
            var altdiff = end.Alt - start.Alt;
            PointLatLngAlt newpos = PointLatLngAlt.Zero;

            while (distout < maxdist)
            {
                // get a projected point to test intersection against - not using slope distance
                PointLatLngAlt terrainstart = start.newpos(bearing, distout);
                terrainstart.Alt = srtm.getAltitude(terrainstart.Lat, terrainstart.Lng).alt;

                // get another point stepsize infront
                PointLatLngAlt terrainend = start.newpos(bearing, distout + stepsize);
                terrainend.Alt = srtm.getAltitude(terrainend.Lat, terrainend.Lng).alt;

                // x is dist from start, y is alt
                var newpoint = FindLineIntersection(new PointF(0, (float)start.Alt),
                    new PointF((float)maxdist, (float)end.Alt),
                    new PointF((float)distout, (float)terrainstart.Alt),
                    new PointF((float)distout + stepsize, (float)terrainend.Alt));

                if (newpoint.X != 0)
                {
                    newpos = start.newpos(bearing, newpoint.X);
                    newpos.Alt = newpoint.Y;
                    break;
                }

                distout += stepsize;
            }

            if (newpos == PointLatLngAlt.Zero)
                newpos = end;

            return newpos;
        }

        class PointF
        {
            internal PointF()
            {
            }

            internal  PointF(float X, float Y)
            {
                this.X = X;
                this.Y = Y;
            }

            internal  float Y { get; set; }

            internal  float X { get; set; }
        }

        static PointF FindLineIntersection(PointF start1, PointF end1, PointF start2, PointF end2)
        {
            double denom = ((end1.X - start1.X) * (end2.Y - start2.Y)) - ((end1.Y - start1.Y) * (end2.X - start2.X));
            //  AB & CD are parallel         
            if (denom == 0)
                return new PointF();
            double numer = ((start1.Y - start2.Y) * (end2.X - start2.X)) - ((start1.X - start2.X) * (end2.Y - start2.Y));
            double r = numer / denom;
            double numer2 = ((start1.Y - start2.Y) * (end1.X - start1.X)) - ((start1.X - start2.X) * (end1.Y - start1.Y));
            double s = numer2 / denom;
            if ((r < 0 || r > 1) || (s < 0 || s > 1))
                return new PointF();
            // Find intersection point      
            PointF result = new PointF();
            result.X = (float)(start1.X + (r * (end1.X - start1.X)));
            result.Y = (float)(start1.Y + (r * (end1.Y - start1.Y)));
            return result;
        }

        static MemoryStream readFile(string filename)
        {
            log.Info(filename);

            if (filecache.ContainsKey(filename))
            {
                return (MemoryStream) filecache[filename];
            }
            else
            {
                FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read);

                byte[] file = new byte[fs.Length];
                fs.Read(file, 0, (int) fs.Length);

                filecache[filename] = new MemoryStream(file);

                fs.Close();

                return (MemoryStream) filecache[filename];
            }
        }

        static SemaphoreSlim requestSemaphore = new SemaphoreSlim(1);

        static async Task requestRunner()
        {
            log.Info("requestRunner start");

            requestThreadrun = true;

            while (requestThreadrun)
            {
                try
                {
                    await requestSemaphore.WaitAsync(30000).ConfigureAwait(false);

                    string item = "";
                    lock (objlock)
                    {
                        if (queue.Count > 0)
                        {
                            item = queue[0];
                        }
                    }

                    if (item != "")
                    {
                        log.Info(item);
                        await get3secfile(item).ConfigureAwait(false);
                        lock (objlock)
                        {
                            queue.Remove(item);

                            // continue without delay
                            if (queue.Count > 0)
                            {
                                requestSemaphore.Release();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }

                // never more than 1/s
                try
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                catch
                {

                }
            }
        }

        static async Task get3secfile(object name)
        {
            // check file doesnt already exist
            if (File.Exists(datadirectory + Path.DirectorySeparatorChar + (string) name))
            {
                FileInfo fi = new FileInfo(datadirectory + Path.DirectorySeparatorChar + (string) name);
                if (fi.Length != 0)
                    return;
            }

            int checkednames = 0;
            List<string> list = new List<string>();

            // load 1 arc seconds first
            list.Add(baseurl1sec);
            log.Info("srtm1sec " + list.Count);
            // load 3 arc second
            list.AddRange(await getListing(baseurl).ConfigureAwait(false));
            log.Info("srtm1esc+3sec " + list.Count);

            foreach (string item in list)
            {
                List<string> hgtfiles = new List<string>();

                hgtfiles = await getListing(item).ConfigureAwait(false);

                foreach (string hgt in hgtfiles)
                {
                    checkednames++;
                    if (hgt.Contains((string) name))
                    {
                        // get file

                        await gethgt(hgt, (string) name).ConfigureAwait(false);
                        return;
                    }
                }
            }

            // if there are no http exceptions, and the list is >= 9, then everything above is valid
            // 38581 is all srtm3 and srtm1
            if (list.Count >= 9 && checkednames > 38000 && !oceantile.Contains((string) name))
            {
                // we must be an ocean tile - no matchs
                oceantile.Add((string) name);
            }
        }

        public static string baseurl1sec { get; set; }= "https://terrain.ardupilot.org/SRTM1/";

        public static string baseurl { get; set; }= "https://terrain.ardupilot.org/SRTM3/";

        static HttpClient client = new HttpClient();

        static async Task gethgt(string url, string filename)
        {
            try
            {
                log.Info("Get " + url);

                using (var res = await client.GetAsync(url).ConfigureAwait(false))
                using (Stream resstream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (
                    BinaryWriter bw =
                        new BinaryWriter(File.Create(datadirectory + Path.DirectorySeparatorChar + filename + ".zip")))
                {
                    byte[] buf1 = new byte[1024*4];

                    int size = 0;

                    while (resstream.CanRead)
                    {
                        int len = await resstream.ReadAsync(buf1, 0, buf1.Length).ConfigureAwait(false);
                        if (len == 0)
                            break;
                        bw.Write(buf1, 0, len);

                        size += len;
                    }
                    bw.Flush();
                    bw.Close();

                    log.Info("Got " + url + " " + size);

                    FastZip fzip = new FastZip();

                    lock(extract)
                        fzip.ExtractZip(datadirectory + Path.DirectorySeparatorChar + filename + ".zip", datadirectory, "");

                    fzip = null;
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        static async Task<List<string>> getListing(string url)
        {
            List<string> list = new List<string>();

            if (url.EndsWith("bios"))
                return list;

            string name = new Uri(url).AbsolutePath;

            name = Path.GetFileName(name.TrimEnd('/'));

            if (File.Exists(datadirectory + Path.DirectorySeparatorChar + name))
            {
                var fi = new FileInfo(datadirectory + Path.DirectorySeparatorChar + name);
                if (fi.Length > 0 && fi.LastWriteTime.AddDays(7) > DateTime.Now)
                {
                    using (StreamReader sr = new StreamReader(datadirectory + Path.DirectorySeparatorChar + name))
                    {
                        while (!sr.EndOfStream)
                        {
                            list.Add(sr.ReadLine());
                        }

                        sr.Close();
                    }
                    return list;
                }
            }

            try
            {
                log.Info("srtm req " + url);

                using (var res = await client.GetAsync(url).ConfigureAwait(false))
                using (StreamReader resstream = new StreamReader(await res.Content.ReadAsStreamAsync().ConfigureAwait(false)))
                {

                    string data = await resstream.ReadToEndAsync().ConfigureAwait(false);

                    Regex regex = new Regex("href=\"([^\"]+)\"", RegexOptions.IgnoreCase);
                    if (regex.IsMatch(data))
                    {
                        MatchCollection matchs = regex.Matches(data);
                        for (int i = 0; i < matchs.Count; i++)
                        {
                            if (matchs[i].Groups[1].Value.ToString().Contains(".."))
                                continue;
                            if (matchs[i].Groups[1].Value.ToString().Contains("http"))
                                continue;
                            if (matchs[i].Groups[1].Value.ToString().EndsWith("/srtm/version2_1/"))
                                continue;

                            list.Add(url.TrimEnd(new char[] { '/', '\\' }) + "/" + matchs[i].Groups[1].Value.ToString());
                        }
                    }
                }

                using (StreamWriter sw = new StreamWriter(datadirectory + Path.DirectorySeparatorChar + name))
                {
                    list.ForEach(x =>
                    {
                        sw.WriteLine((string) x);
                    });

                    if (name.Equals("README.txt") || name.Equals("Region_definition.jpg"))
                        sw.Write(" ");

                    sw.Close();
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
                throw;
            }

            return list;
        }

        public void Dispose()
        {
            requestThreadrun = false;
        }
    }
}