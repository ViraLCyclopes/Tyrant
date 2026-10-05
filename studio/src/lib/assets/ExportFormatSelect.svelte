<script lang="ts">
  import { exportFormat, setExportFormat, type ExportFormat } from './exportFormat.svelte';
</script>

<select
  aria-label="Model format"
  title="Models are exported as glb; FBX is converted by Blender"
  value={exportFormat.value}
  onchange={(e) => setExportFormat(e.currentTarget.value as ExportFormat)}
>
  <option value="glb">glb</option>
  <option value="fbx">FBX</option>
  <option value="both">glb + FBX</option>
</select>
