using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lix.Core;
using Gizmos = Popcron.Gizmos;

/// <summary>
/// A floating-capsule oriented physics based character controller. Based on the approach devised by Toyful Games for Very Very Valet.
/// </summary>
public class PhysicsBasedEnemy : MonoBehaviour
{
  private Transform _target;
  private Rigidbody _rb;
  private Vector3 _gravitationalForce;
  private Vector3 _rayDir = Vector3.down;
  private Vector3 _previousVelocity = Vector3.zero;
  private Vector2 _moveContext;
  private ParticleSystem.EmissionModule _emission;

  [Header("Other:")]
  [SerializeField] private LayerMask _terrainLayer;
  [SerializeField] private ParticleSystem _dustParticleSystem;
  private AudioManager _audioManager;
  private bool _shouldMaintainHeight = true;

  [Header("Height Spring:")]
  [SerializeField] private float _rideHeight = 1.75f; // rideHeight: desired distance to ground (Note, this is distance from the original raycast position (currently centre of transform)). 
  [SerializeField] private float _rayToGroundLength = 3f; // rayToGroundLength: max distance of raycast to ground (Note, this should be greater than the rideHeight).
  [SerializeField] public float _rideSpringStrength = 50f; // rideSpringStrength: strength of spring. (?)
  [SerializeField] private float _rideSpringDamper = 5f; // rideSpringDampener: dampener of spring. (?)
  [SerializeField] private Oscillator _squashAndStretchOcillator;


  private enum lookDirectionOptions { velocity, acceleration, moveInput, isometricAim };
  private Quaternion _uprightTargetRot = Quaternion.identity; // Adjust y value to match the desired direction to face.
  private Quaternion _lastTargetRot;
  private Vector3 _platformInitRot;
  private bool didLastRayHit;

  [Header("Upright Spring:")]
  [SerializeField] private lookDirectionOptions _characterLookDirection = lookDirectionOptions.velocity;
  [SerializeField] private float _uprightSpringStrength = 40f;
  [SerializeField] private float _uprightSpringDamper = 5f;

  private float _speedFactor = 1f;
  private float _maxAccelForceFactor = 1f;
  private Vector3 _m_GoalVel = Vector3.zero;

  [Header("Movement:")]
  [SerializeField] private float _maxSpeed = 8f;
  [SerializeField] private float _acceleration = 200f;
  [SerializeField] private float _maxAccelForce = 150f;
  [SerializeField] private float _leanFactor = 0.25f;
  [SerializeField] private AnimationCurve _accelerationFactorFromDot;
  [SerializeField] private AnimationCurve _maxAccelerationForceFactorFromDot;
  [SerializeField] private Vector3 _moveForceScale = new Vector3(1f, 0f, 1f);


  // Slow
  private bool _isSlowed = false;
  private float _slowFactor = 0.5f;

  /// <summary>
  /// Prepare frequently used variables.
  /// </summary>
  private void Awake()
  {
    _audioManager = ServiceLocator.Get<AudioManager>();

    _rb = GetComponent<Rigidbody>();
    _gravitationalForce = Physics.gravity * _rb.mass;

    if (_dustParticleSystem)
    {
      _emission = _dustParticleSystem.emission; // Stores the module in a local variable
      _emission.enabled = false; // Applies the new value directly to the Particle System
    }
  }

  private void Start()
  {
    _target = ServiceLocator.Get<Human>().transform;
  }

  /// <summary>
  /// Use the result of a Raycast to determine if the capsules distance from the ground is sufficiently close to the desired ride height such that the character can be considered 'grounded'.
  /// </summary>
  /// <param name="rayHitGround">Whether or not the Raycast hit anything.</param>
  /// <param name="rayHit">Information about the ray.</param>
  /// <returns>Whether or not the player is considered grounded.</returns>
  private bool CheckIfGrounded(bool rayHitGround, RaycastHit rayHit)
  {
    bool grounded;
    if (rayHitGround == true)
    {
      grounded = rayHit.distance <= _rideHeight * 1.3f; // 1.3f allows for greater leniancy (as the value will oscillate about the rideHeight).
    }
    else
    {
      grounded = false;
    }
    return grounded;
  }

  /// <summary>
  /// Gets the look desired direction for the character to look.
  /// The method for determining the look direction is depends on the lookDirectionOption.
  /// </summary>
  /// <param name="lookDirectionOption">The factor which determines the look direction: velocity, acceleration or moveInput.</param>
  /// <returns>The desired look direction.</returns>
  private Vector3 GetLookDirection(lookDirectionOptions lookDirectionOption)
  {
    Vector3 lookDirection = Vector3.zero;
    if (lookDirectionOption == lookDirectionOptions.velocity || lookDirectionOption == lookDirectionOptions.acceleration)
    {
      Vector3 velocity = _rb.velocity;
      velocity.y = 0f;
      if (lookDirectionOption == lookDirectionOptions.velocity)
      {
        lookDirection = velocity;
      }
      else if (lookDirectionOption == lookDirectionOptions.acceleration)
      {
        Vector3 deltaVelocity = velocity - _previousVelocity;
        _previousVelocity = velocity;
        Vector3 acceleration = deltaVelocity / Time.fixedDeltaTime;
        lookDirection = acceleration;
      }
    }

    return lookDirection;
  }

  private bool _prevGrounded = false;
  /// <summary>
  /// Determines and plays the appropriate character sounds, particle effects, then calls the appropriate methods to move and float the character.
  /// </summary>
  private void FixedUpdate()
  {
    if (_target == null)
    {
      return;
    }

    (bool rayHitGround, RaycastHit rayHit) = RaycastToGround();

    // vector to the target
    Vector3 _moveInput = _target.position - transform.position;
    _moveInput.y = 0f;
    _moveInput.Normalize();

    bool grounded = CheckIfGrounded(rayHitGround, rayHit);
    if (grounded == true)
    {
      if (_prevGrounded == false)
      {
        if (_audioManager.IsPlaying("Land"))
        {
          _audioManager.Play("Land");
        }

      }

      if (_moveInput.magnitude != 0)
      {
        if (_audioManager.IsPlaying("Walking"))
        {
          _audioManager.Play("Walking");
        }
      }
      else
      {
        _audioManager.Stop("Walking");
      }

      if (_dustParticleSystem)
      {
        if (_emission.enabled == false)
        {
          _emission.enabled = true; // Applies the new value directly to the Particle System                  
        }
      }
    }
    else
    {
      _audioManager.Stop("Walking");

      if (_dustParticleSystem)
      {
        if (_emission.enabled == true)
        {
          _emission.enabled = false; // Applies the new value directly to the Particle System
        }
      }

      return;
    }

    CharacterMove(_moveInput, rayHit);

    if (rayHitGround && _shouldMaintainHeight)
    {
      MaintainHeight(rayHit);
    }

    Vector3 lookDirection = GetLookDirection(_characterLookDirection);
    MaintainUpright(lookDirection, rayHit);

    _prevGrounded = grounded;
  }

  /// <summary>
  /// Perfom raycast towards the ground.
  /// </summary>
  /// <returns>Whether the ray hit the ground, and information about the ray.</returns>
  private (bool, RaycastHit) RaycastToGround()
  {
    RaycastHit rayHit;
    Ray rayToGround = new Ray(transform.position, _rayDir);
    bool rayHitGround = Physics.Raycast(rayToGround, out rayHit, _rayToGroundLength, _terrainLayer.value);
    //Debug.DrawRay(transform.position, _rayDir * _rayToGroundLength, Color.blue);
    return (rayHitGround, rayHit);
  }

  /// <summary>
  /// Determines the relative velocity of the character to the ground beneath,
  /// Calculates and applies the oscillator force to bring the character towards the desired ride height.
  /// Additionally applies the oscillator force to the squash and stretch oscillator, and any object beneath.
  /// </summary>
  /// <param name="rayHit">Information about the RaycastToGround.</param>
  private void MaintainHeight(RaycastHit rayHit)
  {
    Vector3 vel = _rb.velocity;
    Vector3 otherVel = Vector3.zero;
    Rigidbody hitBody = rayHit.rigidbody;
    if (hitBody != null)
    {
      otherVel = hitBody.velocity;
    }
    float rayDirVel = Vector3.Dot(_rayDir, vel);
    float otherDirVel = Vector3.Dot(_rayDir, otherVel);

    float relVel = rayDirVel - otherDirVel;
    float currHeight = rayHit.distance - _rideHeight;
    float springForce = (currHeight * _rideSpringStrength) - (relVel * _rideSpringDamper);
    Vector3 maintainHeightForce = -_gravitationalForce + springForce * Vector3.down;
    Vector3 oscillationForce = springForce * Vector3.down;
    _rb.AddForce(maintainHeightForce);
    _squashAndStretchOcillator.ApplyForce(oscillationForce);
    //Debug.DrawLine(transform.position, transform.position + (_rayDir * springForce), Color.yellow);

    // Apply force to objects beneath
    if (hitBody != null)
    {
      hitBody.AddForceAtPosition(-maintainHeightForce, rayHit.point);
    }
  }

  /// <summary>
  /// Determines the desired y rotation for the character, with account for platform rotation.
  /// </summary>
  /// <param name="yLookAt">The input look rotation.</param>
  /// <param name="rayHit">The rayHit towards the platform.</param>
  private void CalculateTargetRotation(Vector3 yLookAt, RaycastHit rayHit = new RaycastHit())
  {
    if (didLastRayHit)
    {
      _lastTargetRot = _uprightTargetRot;
      try
      {
        _platformInitRot = transform.parent.rotation.eulerAngles;
      }
      catch
      {
        _platformInitRot = Vector3.zero;
      }
    }
    if (rayHit.rigidbody == null)
    {
      didLastRayHit = true;
    }
    else
    {
      didLastRayHit = false;
    }

    if (yLookAt != Vector3.zero)
    {
      _uprightTargetRot = Quaternion.LookRotation(yLookAt, Vector3.up);
      _lastTargetRot = _uprightTargetRot;
      try
      {
        _platformInitRot = transform.parent.rotation.eulerAngles;
      }
      catch
      {
        _platformInitRot = Vector3.zero;
      }
    }
    else
    {
      try
      {
        Vector3 platformRot = transform.parent.rotation.eulerAngles;
        Vector3 deltaPlatformRot = platformRot - _platformInitRot;
        float yAngle = _lastTargetRot.eulerAngles.y + deltaPlatformRot.y;
        _uprightTargetRot = Quaternion.Euler(new Vector3(0f, yAngle, 0f));
      }
      catch
      {

      }
    }
  }

  /// <summary>
  /// Adds torque to the character to keep the character upright, acting as a torsional oscillator (i.e. vertically flipped pendulum).
  /// </summary>
  /// <param name="yLookAt">The input look rotation.</param>
  /// <param name="rayHit">The rayHit towards the platform.</param>
  private void MaintainUpright(Vector3 yLookAt, RaycastHit rayHit = new RaycastHit())
  {
    CalculateTargetRotation(yLookAt, rayHit);

    Quaternion currentRot = transform.rotation;
    Quaternion toGoal = MathsUtils.ShortestRotation(_uprightTargetRot, currentRot);

    Vector3 rotAxis;
    float rotDegrees;

    toGoal.ToAngleAxis(out rotDegrees, out rotAxis);
    rotAxis.Normalize();

    float rotRadians = rotDegrees * Mathf.Deg2Rad;

    _rb.AddTorque((rotAxis * (rotRadians * _uprightSpringStrength)) - (_rb.angularVelocity * _uprightSpringDamper));
  }

  /// <summary>
  /// Apply forces to move the character up to a maximum acceleration, with consideration to acceleration graphs.
  /// </summary>
  /// <param name="moveInput">The player movement input.</param>
  /// <param name="rayHit">The rayHit towards the platform.</param>
  private void CharacterMove(Vector3 moveInput, RaycastHit rayHit)
  {
    Vector3 m_UnitGoal = moveInput;
    Vector3 unitVel = _m_GoalVel.normalized;
    float velDot = Vector3.Dot(m_UnitGoal, unitVel);
    float accel = _acceleration * _accelerationFactorFromDot.Evaluate(velDot);
    Vector3 goalVel = m_UnitGoal * _maxSpeed * _speedFactor;
    if (_isSlowed)
    {
      goalVel *= _slowFactor;
    }
    Rigidbody hitBody = rayHit.rigidbody;
    _m_GoalVel = Vector3.MoveTowards(_m_GoalVel,
                                    goalVel,
                                    accel * Time.fixedDeltaTime);
    Vector3 neededAccel = (_m_GoalVel - _rb.velocity) / Time.fixedDeltaTime;
    float maxAccel = _maxAccelForce * _maxAccelerationForceFactorFromDot.Evaluate(velDot) * _maxAccelForceFactor;
    neededAccel = Vector3.ClampMagnitude(neededAccel, maxAccel);

    Vector3 force = Vector3.Scale(neededAccel * _rb.mass, _moveForceScale);
    Vector3 position = transform.position + new Vector3(0f, transform.localScale.y * _leanFactor, 0f);

    _rb.AddForceAtPosition(force, position); // Using AddForceAtPosition in order to both move the player and cause the play to lean in the direction of input.
  }

  public void Slow(float slowFactor, float slowDuration)
  {
    _slowFactor = slowFactor;
    _isSlowed = true;
    StartCoroutine(SlowTimer(slowDuration));
  }

  private IEnumerator SlowTimer(float slowDuration)
  {
    yield return new WaitForSeconds(slowDuration);
    _isSlowed = false;
  }
}
