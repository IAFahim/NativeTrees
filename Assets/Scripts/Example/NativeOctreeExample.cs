using System;
using System.Diagnostics;
using AnyPath.Graphs.Extra;
using Example;
using NativeTrees;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

public class NativeOctreeExample : MonoBehaviour
{
    [BurstCompile(CompileSynchronously = true)]
    struct PopulateJob : IJob
    {
        public NativeArray<Triangle> actualTriangles;
        public NativeArray<AABB> actualBounds;

        public NativeOctree<Triangle> octree;
        public void Execute()
        {
            for (int i = 0; i < actualTriangles.Length; i++)
                octree.Insert(actualTriangles[i], actualBounds[i]);
            
            //for (int i = 0; i < actualTriangles.Length; i++)
            //    octree.InsertPoint(actualTriangles[i], actualBounds[i].min);
        }
    }
    
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private new Camera camera;
    [SerializeField] private Transform hitTransform;
    [SerializeField] private Transform nearestTransform;

    private NativeOctree<Triangle> octree;
    private NativeList<Triangle> list;
    private NativeOctree<Triangle>.NearestNeighbourQuery query;

    private void Start()
    {
        var mesh = meshFilter.mesh;

        octree = new NativeOctree<Triangle>(mesh.bounds, 32, 10, Allocator.Persistent);
        list = new NativeList<Triangle>(Allocator.Persistent);
        query = new NativeOctree<Triangle>.NearestNeighbourQuery(Allocator.Persistent);

        var triangles = mesh.triangles;
        var vertices = mesh.vertices;

        var sw = Stopwatch.StartNew();
        var actualTriangles = new NativeArray<Triangle>(triangles.Length / 3, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        var actualBounds = new NativeArray<AABB>(actualTriangles.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        
        int n = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Triangle tri = new Triangle(
                vertices[triangles[i]], 
                vertices[triangles[i + 1]], 
                vertices[triangles[i + 2]], n);
            
            actualBounds[n] = tri.GetAABB();
            actualTriangles[n++] = tri;
        }
        Debug.Log($"Triangles converted in {sw.Elapsed.TotalMilliseconds}ms");
        sw.Restart();

        
        for (int i = 0; i < actualTriangles.Length; i++)
        {
            octree.Insert(actualTriangles[i], actualBounds[i]);
            //var tri = actualTriangles[i];
            //octree.InsertPoint(tri, tri.a);
        }

        /*
        var job = new PopulateJob()
        {
            octree = octree,
            actualBounds = actualBounds,
            actualTriangles = actualTriangles
        };
        job.Run();
        */

        Debug.Log($"Octree constructed in {sw.Elapsed.TotalMilliseconds}ms");

        actualTriangles.Dispose();
        actualBounds.Dispose();
    }

    private void OnDestroy()
    {
        octree.Dispose();
        list.Dispose();
        query.Dispose();
    }

    private void Update()
    {
        var ray = camera.ScreenPointToRay(Input.mousePosition);

        var sw = Stopwatch.StartNew();
        bool didHit = octree.Raycast<TriangleRayIntersecter>(ray, out var hit);
        
        if (didHit)
        {
            Debug.Log($"(Non burst) Raycast performed in {sw.Elapsed.TotalMilliseconds}ms");
            hitTransform.position = hit.point;
            hitTransform.up = hit.obj.Normal;
            
            /*
            list.Clear();
            sw.Restart();
            octree.RangeAABB(new AABB(hit.point - new float3(5,5,5), hit.point + new float3(5,5,5)), list);
            Debug.Log($"(Non burst) Range performed in {sw.Elapsed.TotalMilliseconds}ms");
            Debug.Log($"Range count {list.Length}");
            */   
        }

        // Pick a random point for our NN query
        float3 point = new float3(
            Random.Range(octree.Bounds.min.x, octree.Bounds.max.x),
            Random.Range(octree.Bounds.min.y, octree.Bounds.max.y),
            Random.Range(octree.Bounds.min.z, octree.Bounds.max.z));

        float maxDistance = 100;

        sw.Restart();
        if (query.TryGetNearestAABB(ref octree, point, maxDistance, out var nearestTri))
        {
            Debug.Log($"(Non burst) Nearest performed in {sw.Elapsed.TotalMilliseconds}ms");
            nearestTransform.position = nearestTri.Centroid;
            nearestTransform.up = nearestTri.Normal;
        }

        NativeReference<Triangle> tempResult = new NativeReference<Triangle>(Allocator.TempJob);
        var newJob = new NewNearestJob()
        {
            octree = octree,
            point = point,
            queryCache = query,
            nearest = tempResult
        };
        sw.Restart();
        newJob.Run();
        Debug.Log($"(Burst) nearest performed in {sw.Elapsed.TotalMilliseconds}ms");
        tempResult.Dispose();
    }

    [BurstCompile]
    struct NewNearestJob : IJob
    {
        public float3 point;
        public NativeOctree<Triangle> octree;
        public NativeOctree<Triangle>.NearestNeighbourQuery queryCache;
        public NativeReference<Triangle> nearest;
        
        public void Execute()
        {
            queryCache.TryGetNearestAABB(ref octree, point, 50, out var nearestTri);
            nearest.Value = nearestTri;
        }
    }
}