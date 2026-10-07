import { dotnet } from './_framework/dotnet.js';

const result = document.getElementById('result');

try {
  const { getAssemblyExports, getConfig } = await dotnet.create();
  const exports = await getAssemblyExports(getConfig().mainAssemblyName);
  const report = exports.Pigeon.Wasm.Harness.DeterminismChecks.Run();

  result.textContent = report;
  document.body.dataset.status = report.startsWith('PASS') ? 'pass' : 'fail';
} catch (error) {
  result.textContent = `ERROR\n${error?.stack ?? error}`;
  document.body.dataset.status = 'error';
}
