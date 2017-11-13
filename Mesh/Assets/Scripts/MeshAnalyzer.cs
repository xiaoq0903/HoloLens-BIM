﻿using HoloToolkit.Unity.SpatialMapping;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MeshAnalyzer : MonoBehaviour
{
    private static readonly Vector3 DOWN = Vector3.down;
    private static readonly Vector3 UP = Vector3.up;
    private static readonly Vector3 FORWARD = Vector3.forward;
    private const float UPPER_LIMIT = 2f;
    private const float UPPER_PLANE_LIMIT = .05f;
    private const int UPPER_ANGLE = 20;

    public HashSet<Vector3> downNormals = new HashSet<Vector3>();
    public HashSet<Vector3> upNormals = new HashSet<Vector3>();
    private HashSet<Line> horizontalNormals = new HashSet<Line>();

    public float scanTime = 30.0f;
    public int numberOfPlanesToFind = 4;

    [Range(0.0f, 1.0f)]
    public float normalsScale = 1.0f;

    public bool drawUpNormals = false;
    public Color colorUpNormals = Color.red;
    public bool drawDownNormals = false;
    public Color colorDownNormals = Color.blue;
    public bool drawHorizontalNormals = false;
    public Color colorHorizontalNormals = Color.green;
    public bool drawWallNormals = false;
    public Color colorWallNormals = Color.yellow;

    private float drawDuration = 300f;

    private bool analyze = false;

    // Update is called once per frame
    private void Update()
    {
        if (analyze || ((Time.time - SpatialMappingManager.Instance.StartTime) < scanTime))
        {
            // Wait until enough time has passed before processing the mesh.
            Debug.Log("No mesh analysis for now.");
        }
        else
        {
            if (SpatialMappingManager.Instance.IsObserverRunning())
            {
                SpatialMappingManager.Instance.StopObserver();
            }

            analyze = true;
            StartCoroutine(AnalyzeRoutine());
            CategorizeHorizontals(horizontalNormals);
        }
    }

    private IEnumerator AnalyzeRoutine()
    {
        Debug.Log("Start mesh analysis");

        List<MeshFilter> filters = SpatialMappingManager.Instance.GetMeshFilters();
        for (int index = 0; index < filters.Count; index++)
        {
            MeshFilter filter = filters[index];
            if (filter != null && filter.sharedMesh != null)
            {
                Mesh mesh = filter.sharedMesh;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();

                int[] triangles = mesh.triangles;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;

                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 v0 = filter.transform.TransformPoint(vertices[triangles[i]]);
                    Vector3 v1 = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
                    Vector3 v2 = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
                    Vector3 center = (v0 + v1 + v2) / 3;
                    Vector3 dir = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));

                    //categorizeNormal(dir, center);
                }

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 vertice = filter.transform.TransformPoint(vertices[i]);
                    Vector3 normal = filter.transform.TransformDirection(normals[i].normalized);

                    CategorizeNormal(normal, vertice);
                }
            }
            yield return null;
        }

        Debug.Log("Normals categorized! Up's: " + upNormals.Count + " Down's: " + downNormals.Count + " Horizontal's: " + horizontalNormals.Count);

        yield return null;

        //CategorizeHorizontals(horizontalNormals);

        List<Line> availableLines = new List<Line>(horizontalNormals);
        for (int i = 0; i < numberOfPlanesToFind; i++)
        {
            Debug.Log("Available Lines Count: " + availableLines.Count);
            List<Line> lines = FindMostPopulatedPlane(availableLines);
            Debug.Log("Count is " + lines.Count);
            Line plane = lines[0];
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = lines.Count.ToString();
            cube.transform.up = plane.Direction;
            cube.transform.position = plane.Origin;
            cube.transform.localScale = new Vector3(10, UPPER_PLANE_LIMIT, 10);
            availableLines.RemoveAll(l => lines.Contains(l));
            if (drawWallNormals)
            {
                foreach (Line n in lines)
                {
                    Debug.DrawRay(n.Origin, n.Direction.normalized * normalsScale, colorWallNormals, drawDuration, true);
                }
            }
        }
    }

    private void CategorizeNormal(Vector3 normal, Vector3 origin)
    {
        if (AngleInAcceptableRange(UP, normal))
        {
            upNormals.Add(normal);
            if (drawUpNormals)
                Debug.DrawRay(origin, normal.normalized * normalsScale, colorUpNormals, drawDuration);
        }
        else if (AngleInAcceptableRange(DOWN, normal))
        {
            downNormals.Add(normal);
            if (drawDownNormals)
                Debug.DrawRay(origin, normal.normalized * normalsScale, colorDownNormals, drawDuration);
        }
        else if (AngleInAcceptableRange(new Vector3(normal.x, 0, normal.z), normal))
        {
            horizontalNormals.Add(new Line(origin, normal));
            if (drawHorizontalNormals)
                Debug.DrawRay(origin, normal.normalized * normalsScale, colorHorizontalNormals, drawDuration);
        }
    }

    private List<Line> FindMostPopulatedPlane(List<Line> availableLines)
    {
        List<Line> linesInPlane = new List<Line>();

        Line[] lines = new Line[availableLines.Count];
        availableLines.CopyTo(lines);

        for (int i = 0; i < lines.Length; i++)
        {
            Line line = lines[i];
            Plane plane = new Plane(line.Direction, line.Origin);
            List<Line> containedLines = new List<Line>
            {
                line
            };

            foreach (Line l in availableLines)
            {
                float distance = Math.Abs(plane.GetDistanceToPoint(l.Origin));
                if (!l.Equals(line) && distance <= UPPER_PLANE_LIMIT && AngleInAcceptableRange(line.Direction, l.Direction))
                {
                    containedLines.Add(l);
                }
            }

            if (containedLines.Count > linesInPlane.Count)
            {
                linesInPlane = containedLines;
            }
        }


        return linesInPlane;
    }

    private Dictionary<int, List<Line>> CategorizeHorizontalLines(HashSet<Line> horizontalLines)
    {
        Line[] lines = new Line[horizontalLines.Count];
        horizontalLines.CopyTo(lines);
        Line maxPlane = null;
        int maxCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            Line line = lines[i];
            Plane linePlane = new Plane(line.Direction, line.Origin);

            int count = 0;
            foreach (Line l in horizontalLines)
            {
                if (linePlane.GetDistanceToPoint(l.Origin) <= UPPER_PLANE_LIMIT && Vector3.Angle(line.Direction, l.Direction) <= UPPER_LIMIT)
                {
                    count++;
                }
            }
            if (count > maxCount)
            {
                maxCount = count;
                maxPlane = line;
            }
        }

        return maxPlane;
    }

    private bool AngleInAcceptableRange(Vector3 a, Vector3 b)
    {
        float angle = Vector3.Angle(a, b);

        return angle <= UPPER_LIMIT || angle >= 180 - UPPER_LIMIT;
    }

    private void CategorizeHorizontals(HashSet<Line> horizontalNormals)
    {
        Debug.Log("Start horizontals categorization");
        Dictionary<float, Line> orientationMap = new Dictionary<float, Line>();

        foreach (Line l in horizontalNormals)
        {
            Vector3 n = l.Direction;
            float angle = Vector3.Angle(new Vector3(n.x, 0, n.z), FORWARD);
            if (!orientationMap.ContainsKey(angle))
            {
                orientationMap.Add(angle, l);
            }
        }

        int position = 0;
        HashSet<Line> maxSubset = new HashSet<Line>();

        int thresholdSpace = 4 * UPPER_ANGLE;
        Debug.Log("thresholdSpace: " + thresholdSpace);

        while (position < 90)
        {
            HashSet<Line> subset = new HashSet<Line>();
            for (int i = 0; i < thresholdSpace; i++)
            {
                float index = ((i / UPPER_ANGLE) * 90) + (i % UPPER_ANGLE) + position;
                float upperBound = (index + 0.5f) % 360;
                float lowerBound = (index - 0.5f) % 360;

                IEnumerable<KeyValuePair<float, Line>> matches = orientationMap.Where(kvp => kvp.Key < upperBound && kvp.Key >= lowerBound);

                foreach (KeyValuePair<float, Line> m in matches)
                {
                    if (!subset.Contains(m.Value))
                    {
                        subset.Add(m.Value);
                    }
                }
            }

            Debug.Log("Subset with " + subset.Count + " normals found.");
            if (subset.Count > maxSubset.Count)
            {
                Debug.Log("New max Subset with " + subset.Count + " normals found.");
                maxSubset = subset;
            }

            position++;
        }

        if (drawWallNormals)
        {
            foreach (Line n in maxSubset)
            {
                Debug.DrawRay(n.Origin, n.Direction.normalized * normalsScale, colorWallNormals, drawDuration, true);
            }
        }
    }

    class Vector3CoordComparer : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b)
        {
            if (Mathf.Abs(a.x - b.x) > 0.1) return false;
            if (Mathf.Abs(a.y - b.y) > 0.1) return false;
            if (Mathf.Abs(a.z - b.z) > 0.1) return false;

            return true; //indeed, very close
        }

        public int GetHashCode(Vector3 obj)
        {
            //a cruder than default comparison, allows to compare very close-vector3's into same hash-code.
            return Math.Round(obj.x, 1).GetHashCode()
                 ^ Math.Round(obj.y, 1).GetHashCode() << 2
                 ^ Math.Round(obj.z, 1).GetHashCode() >> 2;
        }
    }

    class Line
    {
        private Vector3 origin;
        private Vector3 direction;

        public Line(Vector3 origin, Vector3 direction)
        {
            this.origin = origin;
            this.direction = direction;
        }

        public Vector3 Origin
        {
            get
            {
                return origin;
            }
        }

        public Vector3 Direction
        {
            get
            {
                return direction;
            }
        }
    }
}
