using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using UnityEngine;
using Photon.Pun;

public class Gun : MonoBehaviourPun, IPunObservable
{
    public enum State
    {
        READY = 0 , FIRE = 1, RELOAD = 2, EMPTY = 3
    }
    //public State state = State.READY;
    public State state {  get; private set; } = State.READY;
    public Transform firepos;
    public ParticleSystem muzzleFlash;  // 총구 화염 이펙트
    public ParticleSystem shellEject;   // 탄피 배출 이펙트
    private LineRenderer lineRenderer;  // 총알 궤적

    public GunData gunData; // 총 데이터 Scriptable Object
    private float fireDistance = 100f;  // 총알이 날아가는 거리(사정거리)
    private AudioSource source;

    public int ammoRemain;      // 남아있는 전체 총알 수
    public int magAmmo;         // 현재 탄창에 남아있는 총알 수
    private float lastFireTime; // 마지막 총알 발사 시간
    private Vector3 hitPosition;// 총알 타격 지점
    private WaitForSeconds shotEffectWS;
    private WaitForSeconds reloadWS;

    // GunData ScriptableObject에 전역으로 선언해서 필요없는 변수
    //public float damage = 20f;      // 총알 데미지
    //public int magCapacity = 25;    // 탄창 용량
    //public float timeBetFire = 0.1f;// 발사 간격
    //public float reloadTime = 1.8f; // 재장전 시간
    //public AudioClip shotClip;
    //public AudioClip reloadClip;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2; // 포지션 갯수를 2로 설정(시작점과 끝점) 끝점은 도착지점 혹은 설정한 최대거리
        lineRenderer.enabled = false;   // 처음엔 비활성화
        shotEffectWS = new WaitForSeconds(0.03f);
        reloadWS = new WaitForSeconds(gunData.reloadTime);
    }

    private void OnEnable()
    {
        // 총 상태 초기화
        ammoRemain = gunData.startAmmoRemain;   // 총의 남은 전체 탄약을 초기화
        magAmmo = gunData.magCapacity;      // 현재 탄창에 남아있는 총알 갯수 초기화
        state = State.READY;        // 총의 상태를 READY로 초기화
        lastFireTime = 0f;          // 마지막 발사시간 초기화
    }

    //void Start()
    //{
    //    // 통신 방식(PhotonView Scripts 컴퍼넌트 설정값)
    //    photonView.Synchronization = ViewSynchronization.ReliableDeltaCompressed;
    //    photonView.ObservedComponents[0] = this;
    //}

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)   // 로컬 오브젝트라면 실행
        {
            stream.SendNext(ammoRemain);
            stream.SendNext(magAmmo);
            stream.SendNext(state);         // 남은 탄약, 탄창내의 탄약, 총의 상태 송신
        }
        else    // 리모트 오브젝트의 경우 실행
        {
            ammoRemain = (int) stream.ReceiveNext();
            magAmmo = (int)stream.ReceiveNext();
            state = (State)stream.ReceiveNext();    // 남은 탄약, 탄창내의 탄약, 총의 상태 수신
        }
    }

    [PunRPC]
    public void AddAmmo(int ammo)
    {
        ammoRemain += ammo;
    }

    public void Fire()
    {
        // 발사 가능 조건검사 함수
        if (state == State.READY && Time.time >= lastFireTime + gunData.timeBetTime)
        {
            lastFireTime = Time.time;   // 현재 시간을 마지막 발사 시간으로 설정
            Shot(); // 실제 발사처리 함수 호출
        }
    }

    private void Shot()
    {
        // 실제 발사 처리 함수
        //RaycastHit hit;
        //Vector3 hitPos = Vector3.zero;
        //if (Physics.Raycast(firepos.position, firepos.forward, out hit, fireDistance))
        //{
        //    I_Damageable target = hit.collider.GetComponent<I_Damageable>();
        //    // 충돌한 오브젝트에서 인터페이스를 찾음
        //    if(target != null)
        //    {
        //        target.OnDamage (gunData.damage, hit.point, hit.normal);
        //        // 충돌한 오브젝트가 I_Damageable을 구현하고 있다면 데미지 처리
        //    }
        //    hitPos = hit.point; // 충돌 지점 저장
        //}
        //else
        //{
        //    hitPos = firepos.position + firepos.forward * fireDistance;
        //    // 충돌이 없으면 사정거리 끝 지점으로 설정
        //}
        // 실제 발사 처리 함수를 호스트에서 대리 실행(네트워크 게임의 경우)
        photonView.RPC("ShotProcessOnServer", RpcTarget.MasterClient);

        magAmmo--;
        if (magAmmo <= 0)
        {
            state = State.EMPTY;    // 탄창이 0이하일 때 비어있는 상태로 변경
        }
    }

    IEnumerator ShotEffect(Vector3 hitPosition)
    {
        // 발사 이펙트 코루틴
        muzzleFlash.Play();
        shellEject.Play();      // 총 발사시 파티클 재생
        source.PlayOneShot(gunData.shotClip ,1.0f);  // gunData의 발사 사운드 호출 후 호출된 사운드 재생
        lineRenderer.SetPosition(0, firepos.position);  // LineRenderer 시작점 설정
        lineRenderer.SetPosition(1, hitPosition);      // LineRenderer 끝점 설정
        lineRenderer.enabled = true;    // 라인렌더러 활성화

        yield return shotEffectWS;
        lineRenderer.enabled = false;   // 0.03초 뒤 비활성화
    }

    [PunRPC]
    private void ShotProcessOnServer()
    {
        RaycastHit hit; // 레이캐스트에 의한 충돌 정보를 저장
        Vector3 hitPosition = Vector3.zero; // 총알이 맞은 곳을 저장할 변수

        if (Physics.Raycast(firepos.position, firepos.forward, out hit, fireDistance))  // 레이캐스트(시작지점, 방향, 충돌정보, 사정거리)
        {
            I_Damageable target = hit.collider.GetComponent<I_Damageable>();    // 레이가 어떠한 물체와 충돌한경우 충동한 상대방으로 부터 I_Damageable 오브젝트 가져오기
            if (target != null)
            {
                target.OnDamage(gunData.damage, hit.point, hit.normal); // 상대방의 OnDamage 함수를 실행시켜서 상대방에게 데미지 주기
            }
            hitPosition = hit.point;    // 레이가 충돌한 위치 저장
        }
        else
        {
            // 레이가 다른 물체와 충돌하지 않았다면, 총알이 최대 사정거리까지 날아갔을때의 위치를 충돌 위치로 사용
            hitPosition = firepos.position + firepos.forward * fireDistance;
        }
        photonView.RPC("ShotEffectProcessOnClients", RpcTarget.All, hitPosition);
    }

    [PunRPC]
    private void ShotEffectProcessOnClients(Vector3 hitPosition)    // 이펙트 코루틴 맵핑
    {
        StartCoroutine(ShotEffect(hitPosition));
    }

    public bool Reload()
    {
        // 재장전 시도 확인 함수
        if (state == State.RELOAD || ammoRemain <= 0 || magAmmo >= gunData.magCapacity)
        {
            return false;   // 이미 재장전 중이거나, 남은 총알이 없거나, 탄창이 가득 찼다면 재장전 불가능
        }
        StartCoroutine(ReloadRoution());
        return true;    // 재장전 가능
    }

    IEnumerator ReloadRoution()
    {
        // 재장전 코루틴
        state = State.RELOAD;   // 재장전 상태로 변경
        source.PlayOneShot(gunData.reloadClip, 1.0f);

        yield return reloadWS;    // gunData ScriptableObject의 값 불러오기

        // 탄창에 채워야 할 총알 수 계산
        int ammoToFill = gunData.magCapacity - magAmmo;
        // 탄창에 채워야 할 탄알이 남은 탄알보다 많다면, 채워야 할 탄알 수를 남은 탄알 수에 맞춰 조정
        if (ammoRemain < ammoToFill)
        {
            ammoToFill = ammoRemain;
        }
        magAmmo += ammoToFill;  // 탄창에 총알 채우기
        ammoRemain -= ammoToFill;   // 남은 총알 수 감소
        state = State.READY;    // 상태를 발사 준비상태로 전환
    }
}
