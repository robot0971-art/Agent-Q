# PorkDamageFlash Setup Guide

## Prerequisites

1. Pork 모델에 Renderer 컴포넌트가 있어야 함 (SkinnedMeshRenderer 또는 MeshRenderer)
2. Material이 Emission을 지원해야 함

## Setup Steps

### 1. Material 설정

1. Pork의 Material 선택
2. Inspector에서 **Emission** 체크박스 활성화
3. Emission 색상은 기본적으로 검정색으로 설정

### 2. Script 적용

1. Pork 게임 오브젝트 선택
2. `PorkDamageFlash.cs` 스크립트를 Add Component
3. 설정 조정:
   - **Flash Color**: 흰색 (RGBA: 255, 255, 255, 255)
   - **Flash Duration**: 0.1 (초)
   - **Flash Intensity**: 2.0 (발광 강도)
   - **Damage Tag**: 무기 오브젝트의 태그 (예: "Weapon")
   - **Use Trigger**: `true` (Trigger 사용) 또는 `false` (Collision 사용)

### 3. Weapon 태그 설정

1. 무기 오브젝트 선택
2. Inspector에서 Tag를 "Weapon"으로 설정 (없으면 Add Tag...)

### 4. Collider 설정

Pork 오브젝트에 Collider가 있어야 함:
- Trigger 사용 시: Is Trigger 체크
- Collision 사용 시: Is Trigger 체크 해제

## Testing

1. Play Mode 실행
2. 무기로 Pork에 공격
3. 흰색 깜빡임이 0.1초간 나타나는지 확인

## 코드 사용 예시

```csharp
// 스크립트에서 데미지 적용 시
PorkDamageFlash damageFlash = GetComponent<PorkDamageFlash>();
damageFlash.TakeDamage(10f); // 데미지 값과 함께 플래시 효과 발생
```

## 주의사항

- MaterialPropertyBlock을 사용하여 성능 최적화
- 여러 Pork가 같은 Material을 공유해도 독립적으로 플래시 가능
- Emission이 지원되는 쉐이더(Standard, URP, HDRP)에서만 작동
