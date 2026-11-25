import { expect, Page, test } from '@playwright/test';
import { advanceBeforeEveryoneIsReady, closeContextGracefully } from './helpers';

async function submitLogin(page: Page, subject: string, displayName: string): Promise<void> {
  await page.getByLabel('Идентификатор сотрудника').fill(subject);
  await page.getByLabel('Отображаемое имя').fill(displayName);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
}

async function login(page: Page, baseURL: string, subject: string, displayName: string): Promise<void> {
  await page.goto(`${baseURL}/login`);
  await submitLogin(page, subject, displayName);
  await expect(page.getByRole('heading', { name: 'Мои встречи' })).toBeVisible();
}

test('M1 templates, meeting, invitation and rotation work in independent contexts', async ({ browser, baseURL }) => {
  const origin = baseURL!;
  const firstContext = await browser.newContext();
  const secondContext = await browser.newContext();
  const thirdContext = await browser.newContext();
  const first = await firstContext.newPage();
  const second = await secondContext.newPage();
  const third = await thirdContext.newPage();

  await login(first, origin, 'employee-a', 'Сотрудник А');
  await login(second, origin, 'employee-b', 'Сотрудник Б');
  await login(third, origin, 'employee-c', 'Сотрудник В');

  await expect(second).toHaveTitle('T-Retro');
  await expect(second.getByRole('link', { name: 'T-Retro, главная' })).toBeVisible();
  const theme = second.getByRole('button', { name: /^(Системная|Светлая|Тёмная) тема$/ });
  const themedRoot = second.locator('tui-root');
  await expect(theme).toHaveAccessibleName('Системная тема');
  await theme.hover();
  await expect(second.getByText('Системная тема', { exact: true })).toBeVisible();
  await second.emulateMedia({ colorScheme: 'dark' });
  await expect(themedRoot).toHaveAttribute('tuiTheme', 'dark');
  await theme.click();
  await second.reload();
  await expect(theme).toHaveAccessibleName('Светлая тема');
  await expect(themedRoot).toHaveAttribute('tuiTheme', 'light');
  await theme.click();
  await second.emulateMedia({ colorScheme: 'light' });
  await expect(themedRoot).toHaveAttribute('tuiTheme', 'dark');
  await theme.click();
  await expect(themedRoot).toHaveAttribute('tuiTheme', 'light');

  await first.emulateMedia({ colorScheme: 'light' });
  await first.getByRole('navigation').getByRole('link', { name: 'Шаблоны' }).click();
  await expect(first.getByRole('navigation').getByRole('link', { name: 'Шаблоны' })).toHaveAttribute('aria-current', 'page');
  await expect(first.locator('main h2')).toHaveText(['Доступные шаблоны', 'Новый шаблон']);
  await first.getByRole('button', { name: 'Создать шаблон' }).click();
  const catalogName = first.getByLabel('Название шаблона');
  await expect(catalogName).toHaveAccessibleDescription('Укажите название шаблона.');
  await expect(first.locator('tui-textfield.field-invalid')).toHaveCSS('outline-width', '2px');
  await catalogName.fill('Личный шаблон');
  await first.getByRole('button', { name: 'Создать шаблон' }).click();
  await expect(first.getByText('Шаблон создан.')).toBeVisible();
  await catalogName.fill('Общий шаблон');
  await first.getByLabel('Название колонки').first().fill('Что попробовать');
  await first.getByLabel('Описание колонки').first().fill('Что проверим в следующем цикле');
  await first.getByLabel('Видимость').selectOption('shared');
  await first.getByRole('button', { name: 'Создать шаблон' }).click();
  await expect(first.getByText('Шаблон создан.')).toBeVisible();

  await second.goto(`${origin}/templates`);
  await expect(second.getByText('Общий шаблон', { exact: true })).toBeVisible();
  await expect(second.getByText('Личный шаблон', { exact: true })).toHaveCount(0);

  await second.getByRole('link', { name: /Мои встречи/ }).click();
  await second.getByRole('link', { name: 'Создать ретро' }).click();
  await expect(second.getByRole('heading', { name: 'Шаблоны ретро' })).toBeVisible();
  await second.getByRole('button', { name: 'Парусник' }).click();
  await expect(second.getByRole('list', { name: 'Колонки шаблона' })).toContainText('Риски');
  await second.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await expect(second.getByRole('list', { name: 'Колонки ретро' })).toContainText('Цель');
  await expect(second.getByLabel('Название колонки')).toHaveCount(0);
  await second.getByRole('button', { name: /К шаблонам/ }).click();
  await second.getByRole('button', { name: 'Личные' }).click();
  await expect(second.getByText('У вас пока нет личных шаблонов.')).toBeVisible();
  await second.getByRole('button', { name: 'Создать личный шаблон' }).click();
  await expect(second.getByRole('heading', { name: 'Новый личный шаблон' })).toBeFocused();
  await second.emulateMedia({ colorScheme: 'dark' });
  await expect(themedRoot).toHaveAttribute('tuiTheme', 'dark');
  await second.getByRole('button', { name: 'Сохранить шаблон' }).click();
  const personalName = second.getByLabel('Название шаблона');
  await expect(personalName).toHaveAttribute('aria-invalid', 'true');
  await expect(personalName).toHaveAccessibleDescription('Укажите название шаблона.');
  await expect(second.locator('tui-textfield.field-invalid')).toHaveCSS('outline-color', 'rgb(255, 77, 77)');
  await personalName.fill('После релиза');
  await expect(personalName).not.toHaveAttribute('aria-invalid', 'true');
  await second.getByLabel('Название колонки').first().fill('Улучшения');
  await second.getByLabel('Описание колонки').first().fill('Идеи после релиза');
  await second.getByLabel('Цвет колонки').first().fill('#4455aa');
  await expect(second.getByRole('button', { name: /Переместить (влево|вправо)/ })).toHaveCount(0);
  const keyboardHandle = second.getByRole('button', { name: 'Переместить колонку Улучшения' });
  await keyboardHandle.press('ArrowRight');
  await expect(second.getByLabel('Название колонки').nth(1)).toHaveValue('Улучшения');
  await keyboardHandle.press('ArrowLeft');
  await expect(second.getByLabel('Название колонки').first()).toHaveValue('Улучшения');
  const handle = second.locator('.drag-grip').first();
  const target = second.locator('.column-row').nth(1);
  await handle.hover();
  const destination = (await target.boundingBox())!;
  await second.mouse.down();
  await second.mouse.move(destination.x + destination.width * 0.8, destination.y + 42, { steps: 12 });
  await second.mouse.up();
  await expect(second.getByLabel('Название колонки').nth(1)).toHaveValue('Улучшения');
  await second.getByRole('button', { name: 'Удалить колонку' }).last().click();
  await second.getByRole('button', { name: 'Добавить колонку' }).click();
  const newColumn = second.getByLabel('Название колонки').last();
  await expect(newColumn).toBeFocused();
  await second.getByRole('button', { name: 'Сохранить шаблон' }).click();
  await expect(newColumn).toHaveAccessibleDescription('Укажите название колонки.');
  await expect(second.locator('tui-textfield.field-invalid')).toHaveCSS('outline-color', 'rgb(255, 77, 77)');
  await newColumn.fill('Договорились');
  await second.getByRole('button', { name: 'Сохранить шаблон' }).click();
  await expect(second.getByRole('heading', { name: 'Новое ретро' })).toBeVisible();
  await expect(second.getByText('После релиза', { exact: true })).toBeVisible();

  await second.reload();
  await second.getByRole('button', { name: 'Личные' }).click();
  await expect(second.getByRole('list', { name: 'Колонки шаблона' }).locator('.column-label')).toHaveText([
    'Перестать', 'Улучшения', 'Договорились',
  ]);
  await second.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await second.getByRole('button', { name: 'Изменить колонки' }).click();
  await expect(second.getByLabel('Цвет колонки').nth(1)).toHaveValue('#4455aa');
  await expect(second.getByLabel('Описание колонки').nth(1)).toHaveValue('Идеи после релиза');
  await second.getByLabel('Название колонки').first().fill('Отменённая колонка');
  await second.getByRole('dialog').getByRole('button', { name: 'Отмена', exact: true }).click();
  await expect(second.getByRole('list', { name: 'Колонки ретро' })).toContainText('Перестать');
  await second.getByRole('button', { name: /К шаблонам/ }).click();

  await third.getByRole('link', { name: 'Создать ретро' }).click();
  await third.getByRole('button', { name: 'Личные' }).click();
  await expect(third.getByText('У вас пока нет личных шаблонов.')).toBeVisible();
  await third.getByRole('button', { name: 'Общие' }).click();
  await expect(third.getByRole('button', { name: 'Общий шаблон' })).toBeVisible();
  await expect(third.getByRole('button', { name: 'После релиза' })).toHaveCount(0);

  await second.getByRole('button', { name: 'Общие' }).click();
  await expect(second.getByRole('list', { name: 'Колонки шаблона' })).toContainText('Что попробовать');
  await second.getByRole('button', { name: 'Выбрать шаблон' }).click();
  await second.getByRole('button', { name: 'Изменить колонки' }).click();
  await expect(second.getByLabel('Название колонки').first()).toHaveValue('Что попробовать');
  await second.getByLabel('Описание колонки').last().fill('Оставляем для этого ретро');
  await second.getByRole('button', { name: 'Сохранить колонки' }).click();
  await expect(second.getByRole('list', { name: 'Колонки ретро' })).toContainText('Оставляем для этого ретро');
  await second.getByLabel('Название встречи').fill('Ретро M1');
  await second.getByRole('button', { name: 'Настройки ретро' }).click();
  await second.getByRole('button', { name: 'Голосование', exact: true }).click();
  await expect(second.getByLabel('Голосов на человека')).toHaveValue('6');
  await second.getByRole('switch', { name: 'Включить голосование' }).uncheck();
  await second.getByRole('dialog').getByRole('button', { name: 'Отмена', exact: true }).click();
  await second.getByRole('button', { name: 'Настройки ретро' }).click();
  await second.getByRole('button', { name: 'Голосование', exact: true }).click();
  await expect(second.getByRole('switch', { name: 'Включить голосование' })).toBeChecked();
  await second.getByRole('switch', { name: 'Включить голосование' }).uncheck();
  await second.getByRole('button', { name: 'Сохранить настройки' }).click();
  await second.getByRole('button', { name: 'Создать ретро', exact: true }).click();
  await expect(second.getByRole('heading', { name: 'Ретро M1', exact: true })).toBeVisible();
  await expect(second.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');
  await expect(second.getByRole('list', { name: 'Этапы ретро' })).not.toContainText('Голосование');
  await expect(second.getByRole('heading', { name: 'Настройки подготовки' })).toHaveCount(0);
  await expect(second.getByRole('banner').getByRole('button', { name: 'Пригласить', exact: true })).toBeVisible();

  await first.goto(`${origin}/templates`);
  const sharedCard = first.locator('article').filter({ hasText: 'Общий шаблон' });
  first.once('dialog', (dialog) => dialog.accept());
  await sharedCard.getByRole('button', { name: 'Удалить' }).click();
  await expect(sharedCard).toHaveCount(0);
  await second.reload();
  await expect(second.locator('.board-column h3')).toHaveText(['Что попробовать', 'Перестать', 'Продолжать']);
  await expect(second.getByRole('region', { name: 'Что попробовать' })).toContainText('Что проверим в следующем цикле');
  await expect(second.getByRole('region', { name: 'Продолжать' })).toContainText('Оставляем для этого ретро');

  await second.getByRole('button', { name: 'Пригласить', exact: true }).click();
  const oldInvite = await second.getByLabel('Ссылка приглашения').inputValue();
  const qr = second.getByRole('img', { name: 'QR-код приглашения' });
  await expect(qr).toHaveAttribute('src', /^data:image\/gif;base64,/);
  const originalQr = await qr.getAttribute('src');
  const download = second.waitForEvent('download');
  await second.getByRole('link', { name: 'Скачать QR-код' }).click();
  expect((await download).suggestedFilename()).toBe('t-retro-invite.gif');
  await second.getByRole('button', { name: 'Закрыть приглашение' }).click();
  await first.getByRole('button', { name: 'Выйти', exact: true }).click();
  await expect(first.getByRole('heading', { name: 'Локальный вход' })).toBeVisible();
  await first.goto(oldInvite);
  await expect(first.getByRole('heading', { name: 'Локальный вход' })).toBeVisible();
  await expect(first).toHaveURL(`${origin}/login`);
  await first.reload();
  await submitLogin(first, 'employee-a', 'Сотрудник А');
  await expect(first.getByRole('heading', { name: 'Вступление во встречу' })).toBeVisible();
  await first.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(first.getByRole('heading', { name: 'Ретро M1', exact: true })).toBeVisible();
  await expect(first.locator('.phase-roadmap [aria-current="step"]')).toHaveText('Сбор карточек');
  await expect(first.getByRole('listitem').filter({ hasText: 'Сотрудник А' })).toBeVisible();

  await second.reload();
  await second.getByRole('button', { name: 'Пригласить', exact: true }).click();
  second.once('dialog', (dialog) => dialog.accept());
  await second.getByRole('button', { name: 'Заменить ссылку' }).click();
  await expect(second.getByText('Версия приглашения 2.')).toBeVisible();
  await expect(qr).not.toHaveAttribute('src', originalQr!);
  const newInvite = await second.getByLabel('Ссылка приглашения').inputValue();
  await second.getByRole('button', { name: 'Закрыть приглашение' }).click();
  expect(newInvite).not.toBe(oldInvite);

  await third.goto(oldInvite);
  await third.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(third.getByText('Ссылка приглашения больше не действует.')).toBeVisible();
  await third.goto(newInvite);
  await third.getByRole('button', { name: 'Вступить во встречу' }).click();
  await expect(third.getByRole('heading', { name: 'Ретро M1', exact: true })).toBeVisible();
  await expect(third.getByRole('listitem').filter({ hasText: 'Сотрудник В' })).toBeVisible();

  for (const page of [first, second]) {
    await expect(page.getByRole('region', { name: 'Что попробовать' })).toContainText('Что проверим в следующем цикле');
  }
  const column = second.getByRole('region', { name: 'Что попробовать' });
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await column.getByLabel('Новая карточка').fill('Тема без голосования');
  await column.getByRole('button', { name: 'Добавить карточку' }).click();
  await expect(column.getByText('Тема без голосования', { exact: true })).toBeVisible();
  await advanceBeforeEveryoneIsReady(second, 'Перейти к группировке');
  await advanceBeforeEveryoneIsReady(second, 'Перейти к обсуждению');
  for (const page of [first, second]) {
    await expect(page.getByRole('heading', { name: 'Текущая тема: Тема без голосования' })).toBeVisible();
    await expect(page.locator('.discussion-queue-item')).toContainText('Всего голосов: 0');
  }
  second.once('dialog', (dialog) => dialog.accept());
  await second.getByRole('button', { name: 'Завершить ретро' }).click();
  const report = first.getByRole('link', { name: 'Скачать PDF' });
  await expect(report).toBeVisible();
  const pdf = await firstContext.request.get(new URL((await report.getAttribute('href'))!, origin).href);
  expect(pdf.ok()).toBeTruthy();
  expect((await pdf.body()).subarray(0, 5).toString()).toBe('%PDF-');

  await Promise.all([firstContext, secondContext, thirdContext].map(closeContextGracefully));
});
