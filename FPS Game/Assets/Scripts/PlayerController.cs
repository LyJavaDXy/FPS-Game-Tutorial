using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class PlayerController : MonoBehaviourPunCallbacks, IDamageable
{
	[SerializeField] Image healthbarImage;
	[SerializeField] Image worldHealthbarImage;
	[SerializeField] GameObject ui;

	[SerializeField] GameObject cameraHolder;

	[SerializeField] float mouseSensitivity, sprintSpeed, walkSpeed, jumpForce, smoothTime;

	[SerializeField] Item[] items;

	int itemIndex;
	int previousItemIndex = -1;

	float verticalLookRotation;
	bool grounded;
	Vector3 smoothMoveVelocity;
	Vector3 moveAmount;

	Rigidbody rb;

	PhotonView PV;

	const float maxHealth = 100f;
	float currentHealth = maxHealth;

	PlayerManager playerManager;

	void Awake()
	{
		rb = GetComponent<Rigidbody>();
		PV = GetComponent<PhotonView>();

		playerManager = PhotonView.Find((int)PV.InstantiationData[0]).GetComponent<PlayerManager>();
	}

	void Start()
	{
		if(PV.IsMine)
		{
			EquipItem(0);
		}
		else
		{
			Destroy(GetComponentInChildren<Camera>().gameObject);
			Destroy(rb);
			Destroy(ui);
		}
	}

	void Update()
	{
		if(!PV.IsMine)
			return;

		Look();
		Move();
		Jump();

		for(int i = 0; i < items.Length; i++)
		{
			if(Input.GetKeyDown((i + 1).ToString()))
			{
				EquipItem(i);
				break;
			}
		}

		if(Input.GetAxisRaw("Mouse ScrollWheel") > 0f)
		{
			if(itemIndex >= items.Length - 1)
			{
				EquipItem(0);
			}
			else
			{
				EquipItem(itemIndex + 1);
			}
		}
		else if(Input.GetAxisRaw("Mouse ScrollWheel") < 0f)
		{
			if(itemIndex <= 0)
			{
				EquipItem(items.Length - 1);
			}
			else
			{
				EquipItem(itemIndex - 1);
			}
		}

		if(Input.GetMouseButtonDown(0))
		{
			items[itemIndex].Use();
		}

		if(transform.position.y < -10f) // Die if you fall out of the world
		{
			Die();
		}
	}

	void Look()
	{
		transform.Rotate(Vector3.up * Input.GetAxisRaw("Mouse X") * mouseSensitivity);

		verticalLookRotation += Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
		verticalLookRotation = Mathf.Clamp(verticalLookRotation, -90f, 90f);

		cameraHolder.transform.localEulerAngles = Vector3.left * verticalLookRotation;
	}

	void Move()
	{
		Vector3 moveDir = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")).normalized;

		moveAmount = Vector3.SmoothDamp(moveAmount, moveDir * (Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed), ref smoothMoveVelocity, smoothTime);
	}

	void Jump()
	{
		if(Input.GetKeyDown(KeyCode.Space) && grounded)
		{
			rb.AddForce(transform.up * jumpForce);
		}
	}

	void EquipItem(int _index)
	{
		if(_index == previousItemIndex)
			return;

		itemIndex = _index;

		items[itemIndex].itemGameObject.SetActive(true);

		if(previousItemIndex != -1)
		{
			items[previousItemIndex].itemGameObject.SetActive(false);
		}

		previousItemIndex = itemIndex;

		if(PV.IsMine)
		{
			Hashtable hash = new Hashtable();
			hash.Add("itemIndex", itemIndex);
			PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
		}
	}

	public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
	{
		if(changedProps.ContainsKey("itemIndex") && !PV.IsMine && targetPlayer == PV.Owner)
		{
			EquipItem((int)changedProps["itemIndex"]);
		}
	}

	public void SetGroundedState(bool _grounded)
	{
		grounded = _grounded;
	}

	void FixedUpdate()
	{
		if(!PV.IsMine)
			return;

		rb.MovePosition(rb.position + transform.TransformDirection(moveAmount) * Time.fixedDeltaTime);
	}

	public void TakeDamage(float damage)
	{
		PV.RPC(nameof(RPC_TakeDamage), PV.Owner, damage);// 告诉这个对象的拥有者（被打的玩家）去执行 RPC_TakeDamage()
														// Photon 会在所有客户端上找到这个玩家的对象实例，但只有拥有者的客户端（操控这个Player的客户端）会真正执行逻辑

	}

	[PunRPC]
	void RPC_TakeDamage(float damage, PhotonMessageInfo info)
	{
		currentHealth -= damage;

		healthbarImage.fillAmount = currentHealth / maxHealth;
		
		// 向所有人广播最新血量，所有客户端对应的那个受伤的人（也就是执行这个RPC_TakeDamage方法的人），要更新自己的最新血量UIworldHealthbarImage，同步状态
		PV.RPC(nameof(RPC_SyncHealth), RpcTarget.All, currentHealth);

		if(currentHealth <= 0)
		{
			Die();
			PlayerManager.Find(info.Sender).GetKill();
		}
	}
	/// <summary>
	/// 更新玩家头上的UI
	/// 流程如下：
	/// 🎯 当 B 射击 A：
	///1️⃣ 客户端 B 调用
	///	PV.RPC(nameof(RPC_TakeDamage), PV.Owner, damage);
	///这个 RPC 发给 Player A 的客户端；
	///所以 只有 A 本地执行 RPC_TakeDamage()；
	///A 更新自己的血量 currentHealth。
	///2️⃣ A 计算完血量后调用：
	///PV.RPC(nameof(RPC_SyncHealth), RpcTarget.All, currentHealth);
	///3️⃣ Photon 把这个 RPC 广播给所有客户端（A、B、C），
	///并且在每个客户端中找到对应的 Player A GameObject 执行该方法。
	///于是结果就是：
	///每个客户端都更新自己场景里 Player A 的血条；
	///但 Player B、C 自己的血量不会动。
	/// </summary>
	/// <param name="health"></param>
	[PunRPC]
	void RPC_SyncHealth(float health)
	{
		// 只更新这个 PV 对应的血条（受伤的那个人），不影响其他玩家，自己的血条自己更新currentHealth
		if (worldHealthbarImage != null)
			worldHealthbarImage.fillAmount = health / maxHealth;
	}

	void Die()
	{
		playerManager.Die();
	}
}