# PorkDamageFlash Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unity에서 Pork(돼지) NPC가 데미지를 입을 때 흰색 발광 깜빡임 효과를 MaterialPropertyBlock으로 구현

**Architecture:** Pork 게임 오브젝트에 PorkDamageFlash 컴포넌트를 부착. Collision/Trigger 이벤트 감지 시 MaterialPropertyBlock으로 Emission 색상을 흰색으로 변경하고 0.1초 후 원래대로 복구.

**Tech Stack:** Unity, C#, MaterialPropertyBlock, Coroutine

---

## File Structure

```
Assets/
└── Scripts/
    └── Pork/
        ├── PorkDamageFlash.cs          # 메인 스크립트
        └── PorkDamageFlashTest.cs      # 테스트 스크립트 (선택)
```

---

## Task 1: Create PorkDamageFlash.cs Script

**Files:**
- Create: `Assets/Scripts/Pork/PorkDamageFlash.cs`

### Step 1.1: Create Directory Structure

```bash
mkdir -p Assets/Scripts/Pork
```

- [ ] **Step 1.2: Write PorkDamageFlash.cs**

```csharp
using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class PorkDamageFlash : MonoBehaviour
{
    [Header("Flash Settings")]
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private float flashIntensity = 2.0f;
    
    [Header("Collision Settings")]
    [SerializeField] private string damageTag = "Weapon";
    [SerializeField] private bool useTrigger = true;
    
    private Renderer _renderer;
    private MaterialPropertyBlock _propertyBlock;
    private Color _originalEmissionColor;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    
    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _propertyBlock = new MaterialPropertyBlock();
        
        // Store original emission color
        _renderer.GetPropertyBlock(_propertyBlock);
        _originalEmissionColor = _propertyBlock.GetColor(EmissionColor);
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!useTrigger) return;
        
        if (other.CompareTag(damageTag) || damageTag == "")
        {
            TriggerFlash();
        }
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (useTrigger) return;
        
        if (collision.gameObject.CompareTag(damageTag) || damageTag == "")
        {
            TriggerFlash();
        }
    }
    
    public void TriggerFlash()
    {
        StopAllCoroutines();
        StartCoroutine(FlashCoroutine());
    }
    
    private System.Collections.IEnumerator FlashCoroutine()
    {
        // Apply flash color
        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(EmissionColor, flashColor * flashIntensity);
        _renderer.SetPropertyBlock(_propertyBlock);
        
        // Wait for duration
        yield return new WaitForSeconds(flashDuration);
        
        // Restore original color
        _renderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor(EmissionColor, _originalEmissionColor);
        _renderer.SetPropertyBlock(_propertyBlock);
    }
    
    // Public method to trigger flash from other scripts
    public void TakeDamage(float damage)
    {
        TriggerFlash();
        // Add damage logic here if needed
    }
}
```

- [ ] **Step 1.3: Verify script syntax**

Open Unity and check for compilation errors in the Console window.

Expected: No errors

- [ ] **Step 1.4: Commit**

```bash
git add Assets/Scripts/Pork/PorkDamageFlash.cs
git commit -m "feat: add PorkDamageFlash script with MaterialPropertyBlock"
```

---

## Task 2: Create Material Setup Documentation

**Files:**
- Create: `Assets/Scripts/Pork/README.md`

- [ ] **Step 2.1: Write setup documentation**

```markdown
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
```

- [ ] **Step 2.2: Commit**

```bash
git add Assets/Scripts/Pork/README.md
git commit -m "docs: add PorkDamageFlash setup guide"
```

---

## Task 3: Create Example/Test Scene

**Files:**
- Create: `Assets/Scenes/PorkDamageFlashTest.unity` (Unity에서 직접 생성)

- [ ] **Step 3.1: Create test scene**

In Unity:
1. File > New Scene
2. Save as `Assets/Scenes/PorkDamageFlashTest.unity`
3. Create a simple test environment:
   - Plane (Ground)
   - Cube or Sphere (as Pork) - 이름을 "Pork"로 설정
   - Another Cube (as Weapon) - 태그를 "Weapon"으로 설정

- [ ] **Step 3.2: Setup Pork object**

1. Create Cube → 이름을 "Pork"로 변경
2. Add Component → `PorkDamageFlash`
3. Material 설정:
   - Create > Material → 이름 "PorkMat"
   - Emission 체크, 색상은 검정
   - Pork 오브젝트에 적용
4. Collider 설정:
   - Box Collider 추가
   - Is Trigger 체크 (또는 체크 해제하고 Rigidbody 추가)

- [ ] **Step 3.3: Setup Weapon object**

1. Create Cube → 이름을 "Weapon"로 변경
2. Tag를 "Weapon"으로 설정
3. Rigidbody 추가 (Use Gravity: true)
4. Collider 추가
5. Position을 Pork 위쪽에 배치 (y: 5)

- [ ] **Step 3.4: Test**

1. Play Mode 실행
2. Weapon이 Pork에 닿을 때 흰색 깜빡임 확인
3. 0.1초 후 원래 색상으로 복구 확인

- [ ] **Step 3.5: Commit**

```bash
git add Assets/Scenes/PorkDamageFlashTest.unity
git add Assets/Scenes/PorkDamageFlashTest.unity.meta
git commit -m "test: add PorkDamageFlash test scene"
```

---

## Task 4: Verification Checklist

- [ ] **Step 4.1: Functional Testing**

Verify:
- [ ] Pork가 Weapon에 닿았을 때 흰색 발광 효과 발생
- [ ] 효과가 정확히 0.1초 동안 지속
- [ ] 효과가 끝나면 원래 색상으로 복구
- [ ] 연속으로 데미지를 입어도 효과가 정상적으로 작동
- [ ] MaterialPropertyBlock을 사용하여 다른 Pork에 영향 없음

- [ ] **Step 4.2: Performance Check**

Verify:
- [ ] 여러 Pork가 동시에 플래시할 때 프레임 드롭 없음
- [ ] Material이 공유되어도 각 Pork가 독립적으로 플래시

---

## Notes

### Alternative Approaches (not implemented)

1. **Shader Graph Approach**: 더 복잡한 시각 효과 가능하지만 더 무거움
2. **Animation Event Approach**: 애니메이션과 동기화 필요할 때 유용
3. **Material Swap Approach**: 구현이 간단하지만 Draw Call 증가

### Known Limitations

- MaterialPropertyBlock은 Emission 속성이 있는 쉐이더에서만 작동
- URP/HDRP에서는 `_EmissionColor` 대신 다른 속성명을 사용할 수 있음

---

## Completion Criteria

- [ ] PorkDamageFlash.cs 컴파일 에러 없음
- [ ] 테스트 씬에서 흰색 깜빡임 효과 확인
- [ ] 0.1초 지속 시간 정확
- [ ] README 문서 완성
- [ ] 모든 변경사항 커밋됨
