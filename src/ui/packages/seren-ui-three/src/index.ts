export { default as VRMLookAtController } from './components/VRMLookAtController.vue'
export { default as VRMOutlinePass } from './components/VRMOutlinePass.vue'
export { default as VRMViewer } from './components/VRMViewer.vue'
export { applyViseme, useLipsync, type VisemeTrackFrame } from './composables/useLipsync'
export { useVRM } from './composables/useVRM'
export { useVRMAnimation } from './composables/useVRMAnimation'
export {
  isProceduralGesture,
  useVRMGestures,
  type VRMGestureName,
} from './composables/useVRMGestures'
export { useVRMLookAt, type EyeTrackingMode } from './composables/useVRMLookAt'
