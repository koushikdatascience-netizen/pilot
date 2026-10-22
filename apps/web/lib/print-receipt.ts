export function printReceipt(target: "sale" | "correction") {
  document.body.dataset.printTarget = target;
  window.print();
}
