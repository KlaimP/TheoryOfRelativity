using Godot;
using System;
using System.Collections.Generic;

public partial class GravityManager : Node
{
	private static GravityManager _instance;
	private List<CelestialBody> _celestialBodies = new List<CelestialBody>();
	private List<GravityBody> _gravityBodies = new List<GravityBody>();

	public static GravityManager Instance => _instance;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		_instance = this;

		var bodies = GetTree().GetNodesInGroup("CelestialBody");
		foreach (var body in bodies)
		{
			if (body is CelestialBody celestial)
			{
				_celestialBodies.Add(celestial);
			}
		}
	}
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _PhysicsProcess(double delta)
	{
		
	}

	
}
