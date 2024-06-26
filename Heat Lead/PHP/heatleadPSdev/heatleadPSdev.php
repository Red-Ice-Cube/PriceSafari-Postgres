<?php

if (!defined('_PS_VERSION_')) {
    exit;
}

class HeatLeadPSdev extends Module
{
    private $apiKey;

    public function __construct()
    {
        $this->name = 'heatleadPSdev';
        $this->tab = 'front_office_features';
        $this->version = '1.1.1';
        $this->author = 'HeatLead - Mateusz Werner';
        $this->need_instance = 0;
        $this->apiKey = 'jefo33RPPjdeojpq0582ojajfwp4j29I3MDAQfje3i'; // Dodaj klucz API

        parent::__construct();

        $this->displayName = $this->l('HeatLead Module');
        $this->description = $this->l('Program partnerski HeatLead');
    }

    public function install()
    {
        return parent::install() && $this->registerHook('displayHeader');
    }

    public function hookDisplayHeader($params)
    {
        if (isset($_GET['heatlead'])) {
            $heatleadValue = $_GET['heatlead'];
        
            $this->sendHeatleadToApis($heatleadValue);

            if (isset($_GET['id_order']) && isset($_GET['key'])) {
                $this->setOrderCookies($_GET['id_order'], $_GET['key']);
            }

            $this->handleDataSubmission();
        }
    }

    private function sendHeatleadToApis($heatleadValue)
    {
        $apiResponseEksperci = $this->sendHeatleadToApi($heatleadValue, 'https://eksperci.myjki.com/api/link-loaded');
        $apiResponsePostman = $this->sendHeatleadToApi($heatleadValue, 'https://9dad33cf-9bfe-4544-a7ff-a846893d6522.mock.pstmn.io/post');

        if ($apiResponseEksperci) {
            if (isset($apiResponseEksperci->ttl) && isset($apiResponseEksperci->hltt)) {
                $ttlDays = $apiResponseEksperci->ttl;
                $hltt = $apiResponseEksperci->hltt;
                $expires = time() + (86400 * $ttlDays);
                $this->setHttpOnlyCookie('HLTT', $hltt, $expires);
            } else {
                $this->setHttpOnlyCookie('api_response_error_eksperci', 'Error in API response: Missing ttl or hltt', time() + (86400 * 30));
            }
        } else {
            $this->setHttpOnlyCookie('api_response_error_eksperci', 'Error in API response: Unknown error', time() + (86400 * 30));
        }

        if ($apiResponsePostman) {
            if (isset($apiResponsePostman->ttl) && isset($apiResponsePostman->hltt)) {
                $this->setHttpOnlyCookie('api_response_postman', json_encode($apiResponsePostman), time() + (86400 * 30));
            } else {
                $this->setHttpOnlyCookie('api_response_error_postman', 'Error in API response: Missing ttl or hltt', time() + (86400 * 30));
            }
        } else {
            $this->setHttpOnlyCookie('api_response_error_postman', 'Error in API response: Unknown error', time() + (86400 * 30));
        }
    }

    private function sendHeatleadToApi($heatleadValue, $url)
    {
        $postData = json_encode(array('heatlead' => $heatleadValue));
        $this->setHttpOnlyCookie('api_post_data', $postData, time() + (86400 * 30));

        $ch = curl_init($url);
        curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
        curl_setopt($ch, CURLOPT_HTTPHEADER, array(
            'Content-Type: application/json',
            'Authorization: Bearer ' . $this->apiKey 
        ));
        curl_setopt($ch, CURLOPT_POST, true);
        curl_setopt($ch, CURLOPT_POSTFIELDS, $postData);

        curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);
        curl_setopt($ch, CURLOPT_SSL_VERIFYHOST, false);

        $response = curl_exec($ch);
        $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
        $curlError = curl_error($ch);
        curl_close($ch);

        $this->setHttpOnlyCookie('api_response_body_' . md5($url), $response, time() + (86400 * 30));
        $this->setHttpOnlyCookie('api_http_code_' . md5($url), $httpCode, time() + (86400 * 30));
        $this->setHttpOnlyCookie('api_curl_error_' . md5($url), $curlError, time() + (86400 * 30));
        $this->setHttpOnlyCookie('api_sent_url_' . md5($url), $url, time() + (86400 * 30));

        if ($curlError) {
            return (object) ['error' => 'Curl error: ' . $curlError];
        }

        if ($httpCode !== 200) {
            return (object) ['error' => 'HTTP error: ' . $httpCode, 'response' => $response];
        }

        return json_decode($response);
    }

    private function setHttpOnlyCookie($name, $value, $expires)
    {
        setcookie($name, $value, $expires, "/", "", false, true);
    }

    private function setOrderCookies($orderId, $orderKey)
    {
        $expires = time() + (86400 * 30);
        $this->setHttpOnlyCookie('OrderId', $orderId, $expires);
        $this->setHttpOnlyCookie('OrderKey', $orderKey, $expires);
    }

    private function handleDataSubmission()
    {
        if (isset($_COOKIE['HLTT']) && isset($_COOKIE['OrderId']) && isset($_COOKIE['OrderKey'])) {
            $hltt = $_COOKIE['HLTT'];
            $orderId = $_COOKIE['OrderId'];
            $orderKey = $_COOKIE['OrderKey'];

            if (isset($_GET['id_order'])) {
                $orderId = $_GET['id_order'];
            }
            if (isset($_GET['key'])) {
                $orderKey = $_GET['key'];
            }

            $orderData = $this->getOrderData($orderId);
            if ($orderData && $orderData['secure_key'] == $orderKey) {
                $data = array(
                    "OrderId" => $orderId,
                    "OrderKey" => $orderKey,
                    "HLTT" => $hltt
                );

                $apiResponse1 = $this->postDataToAPI($data, 'https://9dad33cf-9bfe-4544-a7ff-a846893d6522.mock.pstmn.io/post');
                $apiResponse2 = $this->postDataToAPI($data, 'https://eksperci.myjki.com/api/InterceptOrder');

                if ($apiResponse1) {
                    $this->setHttpOnlyCookie('api_response_status_1', $apiResponse1->message, time() + (86400 * 30));
                } else {
                    $this->setHttpOnlyCookie('api_response_error_1', 'Error in API response: Unknown error', time() + (86400 * 30));
                }

                if ($apiResponse2) {
                    $this->setHttpOnlyCookie('api_response_status_2', $apiResponse2->message, time() + (86400 * 30));
                } else {
                    $this->setHttpOnlyCookie('api_response_error_2', 'Error in API response: Unknown error', time() + (86400 * 30));
                }
            }
        }
    }

    private function getOrderData($orderId)
    {
        $sql = new DbQuery();
        $sql->select('id_order, secure_key');
        $sql->from('orders');
        $sql->where('id_order = ' . (int)$orderId);

        return Db::getInstance()->getRow($sql);
    }

    private function postDataToAPI($data, $url)
    {
        $postData = json_encode($data);
        $this->setHttpOnlyCookie('api_post_data', $postData, time() + (86400 * 30));

        $ch = curl_init($url);
        curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
        curl_setopt($ch, CURLOPT_HTTPHEADER, array(
            'Content-Type: application/json; charset=UTF-8',
            'Authorization: Bearer ' . $this->apiKey 
        ));
        curl_setopt($ch, CURLOPT_POST, true);
        curl_setopt($ch, CURLOPT_POSTFIELDS, $postData);

        curl_setopt($ch, CURLOPT_SSL_VERIFYPEER, false);
        curl_setopt($ch, CURLOPT_SSL_VERIFYHOST, false);

        $response = curl_exec($ch);
        $httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
        $curlError = curl_error($ch);
        curl_close($ch);

        $this->setHttpOnlyCookie('api_response_body_' . md5($url), $response, time() + (86400 * 30));
        $this->setHttpOnlyCookie('api_http_code_' . md5($url), $httpCode, time() + (86400 * 30));
        $this->setHttpOnlyCookie('api_curl_error_' . md5($url), $curlError, time() + (86400 * 30));
        $this->setHttpOnlyCookie('api_sent_url_' . md5($url), $url, time() + (86400 * 30));

        if ($curlError || $httpCode !== 200) {
            $errorMessage = $curlError ? 'Curl error: ' . $curlError : 'HTTP error: ' . $httpCode;
            $this->setHttpOnlyCookie('api_response_error', 'Error in API response: ' . $errorMessage . ' | Response: ' . $response, time() + (86400 * 30));
            return null;
        }

        return json_decode($response);
    }
}
?>
