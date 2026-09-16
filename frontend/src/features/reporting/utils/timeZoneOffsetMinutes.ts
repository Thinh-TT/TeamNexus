/**
 * Múi giờ của người xem, dạng **UTC + giá trị này** (đơn vị phút) — đúng thứ `?tzOffsetMinutes=` cần.
 *
 * <para>
 * <b>Cái bẫy:</b> `Date.prototype.getTimezoneOffset()` trả về <b>UTC − local</b>, tức ở Việt Nam
 * (UTC+7) nó là <c>-420</c>. Backend mong đợi <c>UTC + giá trị</c> nên phải **đảo dấu**. Viết sai dấu
 * không làm request lỗi — nó chỉ lặng lẽ dồn mọi thẻ về sai ngày, và biểu đồ vẫn vẽ ra bình thường.
 * </para>
 *
 * <para>
 * Đảo dấu ở **đúng một chỗ**, có test, để không ai phải nhớ quy ước này ở nơi khác.
 * </para>
 */
export function timeZoneOffsetMinutes(date: Date = new Date()): number {
  // `+ 0` chuẩn hoá `-0` (JavaScript sinh ra `-0` khi đảo dấu của `0`) thành `0`. Không có bước này,
  // URL sẽ mang `tzOffsetMinutes=-0`: backend vẫn hiểu đúng, nhưng log/URL trông như có lỗi và
  // `Object.is` so sánh `-0` với `0` là **khác nhau**, đủ để làm một assertion đúng bị đỏ.
  return -date.getTimezoneOffset() + 0
}
