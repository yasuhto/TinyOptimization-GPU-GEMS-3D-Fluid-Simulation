using UnityEngine;
using System.Collections;

namespace FluidSim3DProject
{
	public class SmokeFluidSim : MonoBehaviour 
	{
		//DONT CHANGE THESE
		const int READ = 0;
		const int WRITE = 1;
		const int PHI_N_HAT = 0;
		const int PHI_N_1_HAT = 1;
		
		public enum ADVECTION { NORMAL = 1, BFECC = 2, MACCORMACK = 3 };
		
		//You can change this but you must change the same value in all the compute shader's to the same
		//Must be a pow2 number
		//const int NUM_THREADS = 8;
        private uint KernelThreadSize_X;
        private uint KernelThreadSize_Y;
        private uint KernelThreadSize_Z;

        //You can change this or even use Time.DeltaTime but large time steps can cause numerical errors
        const float TIME_STEP = 0.1f;
		
		public ADVECTION m_advectionType = ADVECTION.NORMAL;
		public int m_width = 128;
		public int m_height = 128;
		public int m_depth = 128;
		public int m_iterations = 10;
		public float m_vorticityStrength = 1.0f;
		public float m_densityAmount = 1.0f;
		public float m_densityDissipation = 0.999f;
		public float m_densityBuoyancy = 1.0f;
		public float m_densityWeight = 0.0125f;
		public float m_temperatureAmount = 10.0f;
		public float m_temperatureDissipation = 0.995f;
		public float m_velocityDissipation = 0.995f;
		public float m_inputRadius = 0.04f;
		public Vector4 m_inputPos = new Vector4(0.5f,0.1f,0.5f,0.0f);
		
		float m_ambientTemperature = 0.0f;
		
		public ComputeShader m_applyImpulse, m_applyAdvect, m_computeVorticity;
		public ComputeShader m_computeDivergence, m_computeJacobi, m_computeProjection;
		public ComputeShader m_computeConfinement, m_computeObstacles, m_applyBuoyancy;
		
		Vector4 m_size;
		DoubleRenderTexture m_velocity;
		DoubleRenderTexture m_vorticity;	//	HACK:2枚も必要ないけど流用
		DoubleRenderTexture m_divergence;   //	HACK:2枚も必要ないけど流用
		DoubleRenderTexture m_pressure;

		/// <summary> x = 温度、y = 密度、zw =（予備）</summary>
		DoubleRenderTexture m_atmosphere;

		DoubleRenderTexture m_obstacles;	//	HACK:2枚も必要ないけど流用

		void Start () 
		{
            m_applyImpulse.GetKernelThreadGroupSizes(0, out KernelThreadSize_X, out KernelThreadSize_Y, out KernelThreadSize_Z);

            //Dimension sizes must be pow2 numbers
            m_width = Mathf.ClosestPowerOfTwo(m_width);
			m_height = Mathf.ClosestPowerOfTwo(m_height);
			m_depth = Mathf.ClosestPowerOfTwo(m_depth);

			//	オブジェクトのスケールを合わせる
			transform.localScale = new Vector3(m_width, m_height, m_depth);
			transform.localPosition = new Vector3(0, m_height / 2, 0);
			
			//Put all dimension sizes in a vector for easy parsing to shader and also prevents user changing
			//dimension sizes during play
			m_size = new Vector4(m_width, m_height, m_depth, 0.0f);
			
			//Create all the buffers needed
			
			int SIZE = m_width*m_height*m_depth;

			m_atmosphere = new DoubleRenderTexture(m_width, m_height, m_depth);
			
			m_velocity = new DoubleRenderTexture(m_width, m_height, m_depth);
			m_vorticity = new DoubleRenderTexture(m_width, m_height, m_depth);
			m_divergence = new DoubleRenderTexture(m_width, m_height, m_depth);
			m_pressure = new DoubleRenderTexture(m_width, m_height, m_depth);
			
			m_obstacles = new DoubleRenderTexture(m_width, m_height, m_depth);

			//Any areas that are obstacles need to be masked of in the obstacle buffer
			//At the moment is only the border around the edge of the buffers to enforce non-slip boundary conditions
			ComputeObstacles();
		}

		void Update()
		{
            float dt = TIME_STEP;

            //First off advect any buffers that contain physical quantities like density or temperature by the 
            //velocity field. Advection is what moves values around.
            ApplyAdvection(dt);

			//The velocity field also advects its self. 
			ApplyAdvectionVelocity(dt);

            //Apply the effect the sinking colder smoke has on the velocity field
            ApplyBuoyancy(dt);

            //Adds a certain amount of density (the visible smoke) and temperate
            ApplyImpulse(dt);

            //The fuild sim math tends to remove the swirling movement of fluids.
            //This step will try and add it back in
            ComputeVorticityConfinement(dt);

            //Compute the divergence of the velocity field. In fluid simulation the
            //fluid is modelled as being incompressible meaning that the volume of the fluid
            //does not change over time. The divergence is the amount the field has deviated from being divergence free
            ComputeDivergence();

			//This computes the pressure need return the fluid to a divergence free condition
			ComputePressure();

			//Subtract the pressure field from the velocity field enforcing the divergence free conditions
			ComputeProjection();

			//rotation of box not support because ray cast in shader uses a AABB intersection
			transform.rotation = Quaternion.identity;

			GetComponent<Renderer>().material.SetVector("_Translate", transform.localPosition);
			GetComponent<Renderer>().material.SetVector("_Scale", transform.localScale);
			GetComponent<Renderer>().material.SetTexture("_Atmosphere", m_atmosphere.Active);
			GetComponent<Renderer>().material.SetVector("_Size", m_size);
		}

		void OnDestroy()
		{
			m_atmosphere.Release();

			m_velocity.Release();
			m_vorticity.Release();
			m_divergence.Release();
			m_pressure.Release();

			m_obstacles.Release();
		}

		private void ComputeObstacles()
		{
			m_computeObstacles.SetVector("_Size", m_size);
			m_computeObstacles.SetTexture(0, "_Write", m_obstacles.Active);
			m_computeObstacles.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);
		}

		private void ApplyImpulse(float dt)
		{
			m_applyImpulse.SetVector("_Size", m_size);
			m_applyImpulse.SetFloat("_Radius", m_inputRadius);
			m_applyImpulse.SetFloat("_DensityAmount", m_densityAmount);
			m_applyImpulse.SetFloat("_TemperatureAmount", m_temperatureAmount);
			m_applyImpulse.SetFloat("_DeltaTime", dt);
			m_applyImpulse.SetVector("_Pos", m_inputPos);
			
			m_applyImpulse.SetTexture(0, "_Read", m_atmosphere.Active);
			m_applyImpulse.SetTexture(0, "_Write", m_atmosphere.Inactive);
			
			m_applyImpulse.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);

			m_atmosphere.Swap();
		}

		private void ApplyBuoyancy(float dt)
		{
			m_applyBuoyancy.SetVector("_Size", m_size);
			m_applyBuoyancy.SetVector("_Up", new Vector4(0,1,0,0));
			m_applyBuoyancy.SetFloat("_Buoyancy", m_densityBuoyancy);
			m_applyBuoyancy.SetFloat("_AmbientTemperature", m_ambientTemperature);
			m_applyBuoyancy.SetFloat("_Weight", m_densityWeight);
			m_applyBuoyancy.SetFloat("_DeltaTime", dt);
			
			m_applyBuoyancy.SetTexture(0, "_Write", m_velocity.Inactive);
			m_applyBuoyancy.SetTexture(0, "_Velocity", m_velocity.Active);
			m_applyBuoyancy.SetTexture(0, "_Atmosphere", m_atmosphere.Active);
			
			m_applyBuoyancy.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);

			m_velocity.Swap();
		}

		private void ApplyAdvection(float dt)
		{
			m_applyAdvect.SetVector("_Size", m_size);
			m_applyAdvect.SetFloat("_DeltaTime", dt);
			m_applyAdvect.SetVector("_Dissipate", new Vector4(m_temperatureDissipation, m_densityDissipation, 0f, 0f));
			m_applyAdvect.SetFloat("_Forward", 1.0f);

			m_applyAdvect.SetTexture(0, "_Read", m_atmosphere.Active);
			m_applyAdvect.SetTexture(0, "_Write", m_atmosphere.Inactive);
			m_applyAdvect.SetTexture(0, "_Velocity", m_velocity.Active);
			m_applyAdvect.SetTexture(0, "_Obstacles", m_obstacles.Active);
			
			m_applyAdvect.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);

			m_atmosphere.Swap();
		}

		private void ApplyAdvectionVelocity(float dt)
		{
			m_applyAdvect.SetVector("_Size", m_size);
			m_applyAdvect.SetFloat("_DeltaTime", dt);
			m_applyAdvect.SetVector("_Dissipate", new Vector4(m_velocityDissipation, m_velocityDissipation, m_velocityDissipation, 0f));
			m_applyAdvect.SetFloat("_Forward", 1.0f);
			
			m_applyAdvect.SetTexture(0, "_Read", m_velocity.Active);
			m_applyAdvect.SetTexture(0, "_Write", m_velocity.Inactive);
			m_applyAdvect.SetTexture(0, "_Velocity", m_velocity.Active);
			m_applyAdvect.SetTexture(0, "_Obstacles", m_obstacles.Active);
			
			m_applyAdvect.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);

			m_velocity.Swap();
		}

		private void ComputeVorticityConfinement(float dt)
		{
			m_computeVorticity.SetVector("_Size", m_size);
			
			m_computeVorticity.SetTexture(0, "_Write", m_vorticity.Active);
			m_computeVorticity.SetTexture(0, "_Velocity", m_velocity.Active);
			
			m_computeVorticity.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);
			
			m_computeConfinement.SetVector("_Size", m_size);
			m_computeConfinement.SetFloat("_DeltaTime", dt);
			m_computeConfinement.SetFloat("_Epsilon", m_vorticityStrength);
			
			m_computeConfinement.SetTexture(0, "_Write", m_velocity.Inactive);
			m_computeConfinement.SetTexture(0, "_Read", m_velocity.Active);
			m_computeConfinement.SetTexture(0, "_Vorticity", m_vorticity.Active);
			
			m_computeConfinement.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);

			m_velocity.Swap();
		}

		private void ComputeDivergence()
		{
			m_computeDivergence.SetVector("_Size", m_size);
			
			m_computeDivergence.SetTexture(0, "_Write", m_divergence.Active);
			m_computeDivergence.SetTexture(0, "_Velocity", m_velocity.Active);
			m_computeDivergence.SetTexture(0, "_Obstacles", m_obstacles.Active);
			
			m_computeDivergence.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);
		}

		private void ComputePressure()
		{
			m_computeJacobi.SetVector("_Size", m_size);
			m_computeJacobi.SetTexture(0, "_Divergence", m_divergence.Active);
			m_computeJacobi.SetTexture(0, "_Obstacles", m_obstacles.Active);
			
			for(int i = 0; i < m_iterations; i++)
			{
				m_computeJacobi.SetTexture(0, "_Write", m_pressure.Inactive);
				m_computeJacobi.SetTexture(0, "_Pressure", m_pressure.Active);
				
				m_computeJacobi.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);

				m_pressure.Swap();
			}
		}

		private void ComputeProjection()
		{
			m_computeProjection.SetVector("_Size", m_size);
			m_computeProjection.SetTexture(0, "_Obstacles", m_obstacles.Active);
			
			m_computeProjection.SetTexture(0, "_Pressure", m_pressure.Active);
			m_computeProjection.SetTexture(0, "_Velocity", m_velocity.Active);
			m_computeProjection.SetTexture(0, "_Write", m_velocity.Inactive);
			
			m_computeProjection.Dispatch(0, (int)m_size.x/(int)KernelThreadSize_X, (int)m_size.y/(int)KernelThreadSize_Y, (int)m_size.z/(int)KernelThreadSize_Z);

			m_velocity.Swap();
		}
    }

}
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
	
