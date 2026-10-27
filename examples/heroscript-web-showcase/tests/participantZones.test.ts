import { expect, it } from "vitest";
import { CardZonePresenter } from "../src/presentation/cardZonePresenter";
import { I18n } from "../src/presentation/i18n";

it("keeps a visible adversary hand out of the player's playable collection", () => {
  const presenter = new CardZonePresenter({ zones: [
    { zoneId: "pocket", ownerId: "hero", allowsCardPlay: true, playerCanPlayFrom: true, contentsVisible: true, cards: [{ cardInstanceId: "mine" }] },
    { zoneId: "pocket", ownerId: "dealer", allowsCardPlay: true, playerCanPlayFrom: false, contentsVisible: true, cards: [{ cardInstanceId: "theirs" }] },
  ] }, new I18n());
  expect(presenter.playableCards().map(card => card.cardInstanceId)).toEqual(["mine"]);
  expect(presenter.auxiliaryZones().map(zone => zone.ownerId)).toEqual(["dealer"]);
});
