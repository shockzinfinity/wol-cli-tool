# Run problem on WSL

> **WSL2 환경 “그 자체”에서 순수 WOL을 직접 성공시키는 방법은 없다.**
> 다만 **WSL에서 실행해도 ‘결과적으로 WOL이 동작하게 만드는 방법’은 있다.**

---

## 1️⃣ 왜 WSL2에서 ‘직접 WOL’은 불가능한가 (구조적 이유)

WOL의 본질:

* **L2 Ethernet broadcast (FF:FF:FF:FF:FF:FF)**
* NIC가 전원이 꺼진 상태에서도 이 프레임을 보고 깨어남
* IP / UDP는 “껍데기”일 뿐, 핵심은 **Ethernet 레벨**

WSL2의 본질:

* Hyper-V 기반 **NAT 가상 네트워크**
* WSL의 `eth0`는 **물리 NIC에 L2로 붙어 있지 않음**
* WSL → LAN 경로는:

  ```
  WSL eth0 → NAT → Windows TCP/IP → LAN
  ```

  이 과정에서 **L2 브로드캐스트 프레임은 생성/전달 자체가 안 됨**

tcpdump:

```
172.22.x.x → 192.168.0.255
```

이건 이미 **L3 패킷**이야.
NIC가 깨울 수 있는 패킷이 아님.

👉 이건 설정 문제 ❌
👉 **아키텍처적 한계 ⛔**

---

## 2️⃣ “그래도 WSL에서 WOL이 되게 하는” 유일한 현실적 방법들

여기서부터는 **정면 돌파가 아니라 우회**야.

---

### 방법 A) WSL → Windows 네트워크 스택을 사용 (가장 단순·확실)

#### 개념

* WSL은 **제어 plane**
* 실제 매직 패킷 전송은 **Windows에서 수행**

#### 구조

```
[ wol CLI (WSL) ]
        ↓
[ Windows PowerShell / native exe ]
        ↓
[ Windows NIC (L2) ]
        ↓
[ LAN ]
```

#### 구현 방식

* WSL에서:

  * `powershell.exe` 또는 `wol.exe (Windows build)` 호출
* CLI 입장에서는:

  * “WSL에서도 실행하면 깨운다” → 사용자 경험 유지

#### 장점

* 추가 장비 없음
* 성공률 거의 100%
* 개발 난이도 낮음

#### 단점

* “순수 리눅스에서만”은 아님 (하지만 현실적으로 대부분 OK)

---

### 방법 B) WOL Relay (가장 깨끗한 아키텍처)

#### 개념

* LAN 안에 항상 켜져 있는 노드 하나
* WSL/Windows/macOS 어디서든 요청 가능
* 릴레이가 L2 브로드캐스트 담당

#### 구조

```
[ wol CLI (WSL/macOS/etc) ]
        ↓ (HTTP / gRPC / MQTT)
[ WOL Relay (LAN 내부) ]
        ↓ (L2 broadcast)
[ Target NIC ]
```

#### 릴레이 후보

* NAS (Synology)
* Raspberry Pi
* 홈 서버
* 스위치에 물린 Linux VM

#### 장점

* 진짜 크로스플랫폼
* 네트워크 제약 명확히 분리
* 기업/홈 환경 모두에서 정석

#### 단점

* 릴레이 하나 필요

---

### 방법 C) 네트워크 장비 기능 사용 (환경 의존)

일부 공유기/스위치:

* “WOL forwarding”
* “Directed broadcast”
* “ARP cache 기반 unicast WOL”

이 경우:

* WSL → 라우터 → LAN
* 성공할 수도 있음

하지만:

* 장비별 편차 큼
* 일반화 불가
* CLI 툴 입장에선 비추천

---

## 3️⃣ “WSL1이면 가능하지 않나?”에 대한 답

* **WSL1**은 Windows 네트워크 스택을 직접 사용
* 이론상:

  * WSL1 → Windows NIC → LAN
  * WOL 성공 가능성 있음

하지만:

* WSL1은 사실상 deprecated
* 최신 환경에서는 WSL2가 기본

👉 “WSL1로 바꿔라”는 해결책은 **현실적이지 않음**

---

## 4️⃣ 네 질문에 대한 정확한 최종 답

> **“궁극적으로 WSL 환경에서 WOL이 동작하도록 하려면?”**

정확한 표현은 이거야:

> **WSL에서 직접 L2 WOL은 불가능하다.
> 대신 WSL을 ‘트리거 환경’으로 두고, L2 접근 가능한 노드에 전송을 위임해야 한다.**

---

## 5️⃣ 그래서 너한테 가장 어울리는 선택

네 대화 흐름, 기술 스택, 목적을 보면:

#### 현실 최적해

1. **CLI는 크로스플랫폼 유지**
2. **WSL2 감지 시**

   * 경고 출력
   * 또는 자동으로 Windows 백엔드 사용
3. 옵션으로:

   * `--backend windows`
   * `--backend relay`
   * `--backend native` (Linux/macOS)

이렇게 설계하면:

* 기술적으로 정직
* 사용자 경험 좋음
* “무색한 크로스플랫폼”이 아니라 **성숙한 플랫폼 대응**

---

### 다음에 바로 할 수 있는 실질적 액션

원하면 다음 중 하나를 바로 구체화해줄게:

* ✔ WSL 감지 코드 + 자동 fallback 로직
* ✔ Windows PowerShell 기반 매직 패킷 전송 코드 (네 CLI에서 바로 호출)
* ✔ NAS/Docker로 띄우는 초경량 WOL relay (10분 셋업)

여기까지 이해했으면, 이건 더 이상 삽질이 아니라 **아키텍처 선택의 문제**야.
