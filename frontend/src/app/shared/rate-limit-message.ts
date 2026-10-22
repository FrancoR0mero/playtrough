export function rateLimitMessage(seconds: number): string {
  const secondLabel = (value: number) => `${value} ${value === 1 ? 'segundo' : 'segundos'}`;
  let duration = secondLabel(seconds);
  if (seconds > 60) {
    const minutes = Math.floor(seconds / 60);
    const rest = seconds % 60;
    duration = `${minutes} ${minutes === 1 ? 'minuto' : 'minutos'}`;
    if (rest) duration += ` y ${secondLabel(rest)}`;
  }
  return `Demasiados intentos. Probá de nuevo en ${duration}`;
}
