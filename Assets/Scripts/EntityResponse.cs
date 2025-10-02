using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

[Serializable]
public class Response
{
    public string CategoryKey;
    public string TextResponse;
    public string AnimationTrigger;

}

[Serializable]
public class EntityResponse
{
    public List<Response> responses;
}

[Serializable]
public class LocationData
{
    public string CategoryKey;
    public string note;
    public string description;
    public string coordinate_system;
    public Locations locations;
}

[Serializable]
public class Locations
{
    public float y;
    public float x;
    public float z;
    public bool isStatic;
    public string relative_to;

    public Vector3 getPosition()
    {
        Vector3 position = new Vector3(x, y, z);
        return position;
    }
}
[Serializable]
public class LocationDatas
{
    public LocationData[] location;
}