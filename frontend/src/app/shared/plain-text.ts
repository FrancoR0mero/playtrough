/** Match the API's newline, control character and whitespace normalization. */
export function normalizePlainText(text: string): string {
  return (
    text
      .replace(/\r\n?/g, '\n')
      // eslint-disable-next-line no-control-regex
      .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/g, '')
      .replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '')
  );
}
